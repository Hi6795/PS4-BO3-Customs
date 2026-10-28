#include "headers.hpp"
#include "t7_config.hpp"
#include "t7_log.hpp"

#include <atomic>

namespace T7Config
{
static const char k_folder[]      = "/data/BO3-Customs";
static const char k_path[]        = "/data/BO3-Customs/config.cfg";
static const char k_temp[]        = "/data/BO3-Customs/config.cfg.tmp";
static const char k_oldBinds[]    = "/data/BO3-Customs/kbm.cfg";
static const char k_oldGraphics[] = "/data/BO3-Customs/graphics.cfg";

constexpr size_t kMaxText = 32768;

static std::atomic<bool> g_busy{false};
static bool              g_migrated = false;
static char              g_current[kMaxText];
static char              g_text[kMaxText];
static bool              g_textOverflow = false;
constexpr int64_t kMissing = -1;
constexpr int64_t kReadFailure = -2;
constexpr int32_t kKernelNotFound = (int32_t)0x80020002;

struct Lock
{
    Lock()
    {
        while (g_busy.exchange(true, std::memory_order_acquire))
            sceKernelUsleep(100);
    }

    ~Lock()
    {
        g_busy.store(false, std::memory_order_release);
    }
};

static int64_t ReadAll(const char* path, char* out, size_t size)
{
    if (!out || size == 0)
        return kReadFailure;

    out[0] = 0;
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
        return fd == kKernelNotFound ? kMissing : kReadFailure;

    size_t used = 0;
    bool failed = false;

    while (used < size - 1)
    {
        const int64_t got = sceKernelRead(fd, out + used, size - 1 - used);

        if (got < 0)
        {
            failed = true;
            break;
        }

        if (got == 0)
            break;

        used += (size_t)got;
    }

    if (used == size - 1)
    {
        char extra;
        failed = sceKernelRead(fd, &extra, 1) != 0;
    }

    if (sceKernelClose(fd) < 0)
        failed = true;

    if (failed)
    {
        out[0] = 0;
        return kReadFailure;
    }

    out[used] = 0;
    return (int64_t)used;
}

static bool WriteAll(const char* text, size_t length)
{
    sceKernelMkdir(k_folder, 0777);

    const int fd = sceKernelOpen(k_temp, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_TRUNC, 0777);

    if (fd < 0)
        return false;

    size_t used = 0;
    while (used < length)
    {
        const int64_t put = sceKernelWrite(fd, text + used, length - used);
        if (put <= 0)
            break;
        used += (size_t)put;
    }

    const bool synced = used == length && sceKernelFsync(fd) >= 0;
    const bool closed = sceKernelClose(fd) >= 0;

    if (!synced || !closed || sceKernelRename(k_temp, k_path) < 0)
    {
        sceKernelUnlink(k_temp);
        return false;
    }

    return true;
}

static void Put(size_t* used, const char* data, size_t length)
{
    if (length >= sizeof(g_text) - *used)
    {
        g_textOverflow = true;
        return;
    }

    memcpy(g_text + *used, data, length);
    *used += length;
    g_text[*used] = 0;
}

static void PutLine(size_t* used, const char* data, size_t length)
{
    Put(used, data, length);
    Put(used, "\n", 1);
}

static void PutBlock(size_t* used, const char* header, const char* lines)
{
    PutLine(used, header, strlen(header));

    const size_t length = strlen(lines);
    Put(used, lines, length);

    if (length && lines[length - 1] != '\n')
        Put(used, "\n", 1);
}

static bool Migrate()
{
    if (g_migrated)
        return true;

    SceKernelStat st;

    const int exists = sceKernelStat(k_path, &st);
    if (exists == 0)
    {
        g_migrated = true;
        return true;
    }
    if (exists != kKernelNotFound)
        return false;

    static char binds[kMaxText / 2];
    static char graphics[4096];
    const int64_t bindsLength = ReadAll(k_oldBinds, binds, sizeof(binds));
    const int64_t graphicsLength = ReadAll(k_oldGraphics, graphics, sizeof(graphics));

    if (bindsLength == kReadFailure || graphicsLength == kReadFailure)
        return false;

    if (bindsLength == kMissing && graphicsLength == kMissing)
    {
        g_migrated = true;
        return true;
    }

    size_t used = 0;
    g_text[0] = 0;
    g_textOverflow = false;

    if (bindsLength > 0)
        PutBlock(&used, "// keyboard and mouse", binds);

    if (graphicsLength > 0)
    {
        PutLine(&used, "// graphics", 11);

        for (char* line = graphics; line && *line;)
        {
            char* const next = strchr(line, '\n');

            if (next)
                *next = 0;

            size_t length = strlen(line);

            while (length && (line[length - 1] == '\r' || line[length - 1] == ' '))
                line[--length] = 0;

            if (length && line[0] != '/' && strchr(line, ' '))
            {
                Put(&used, "set ", 4);
                PutLine(&used, line, length);
            }

            line = next ? next + 1 : nullptr;
        }
    }

    if (g_textOverflow || !WriteAll(g_text, used))
    {
        T7Log_Write("[Config] config.cfg could not be written; kbm.cfg and graphics.cfg stay");
        return false;
    }

    g_migrated = true;

    if (bindsLength >= 0)
        sceKernelUnlink(k_oldBinds);

    if (graphicsLength >= 0)
        sceKernelUnlink(k_oldGraphics);

    T7Log_Write("[Config] kbm.cfg and graphics.cfg are now config.cfg");
    return true;
}
}

bool T7Config_Read(char* out, size_t size)
{
    using namespace T7Config;

    Lock lock;
    if (out && size)
        out[0] = 0;
    return Migrate() && ReadAll(k_path, out, size) >= 0;
}

bool T7Config_Save(const char* section, bool (*owns)(const char* line), const char* lines)
{
    using namespace T7Config;

    Lock lock;
    if (!section || !owns || !lines || !Migrate())
        return false;

    const int64_t got = ReadAll(k_path, g_current, sizeof(g_current));
    if (got == kReadFailure)
    {
        T7Log_Write("[Config] config.cfg could not be read completely; keeping the original");
        return false;
    }
    if (got == kMissing)
        g_current[0] = 0;

    char header[96];
    const int headerLength = snprintf(header, sizeof(header), "// %s", section);
    if (headerLength < 0 || (size_t)headerLength >= sizeof(header))
        return false;

    size_t used = 0;
    bool placed = false;
    g_text[0] = 0;
    g_textOverflow = false;

    for (char* line = g_current; *line;)
    {
        char* const end = strchr(line, '\n');
        size_t length = end ? (size_t)(end - line) : strlen(line);

        if (end)
            *end = 0;

        while (length && line[length - 1] == '\r')
            line[--length] = 0;

        if (strcmp(line, header) == 0 || owns(line))
        {
            if (!placed)
            {
                PutBlock(&used, header, lines);
                placed = true;
            }
        }
        else
        {
            PutLine(&used, line, length);
        }

        if (!end)
            break;

        line = end + 1;
    }

    if (!placed)
        PutBlock(&used, header, lines);

    if (g_textOverflow)
    {
        T7Log_Write("[Config] updated config.cfg is too large; keeping the original");
        return false;
    }
    return WriteAll(g_text, used);
}
