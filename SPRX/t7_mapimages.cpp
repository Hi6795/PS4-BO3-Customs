#include "headers.hpp"
#include "t7_mapimages.hpp"
#include "t7_maps.hpp"
#include "png.hpp"

namespace T7MapImages
{
constexpr uintptr_t kRegisterImage = 0x11C9610;
constexpr uintptr_t kSetImage      = 0x11BE470;
constexpr uintptr_t kDynamicImage  = 0xB61430;
constexpr uintptr_t kFindAsset     = 0x8591B0;
constexpr uintptr_t kElements      = 0xE9831E8;
constexpr uintptr_t kGenerations   = 0xF14B4E8;
constexpr uintptr_t kStreamedDraw  = 0x11DECE0;
constexpr uintptr_t kGpuHeap       = 0x705D9C0;
constexpr uintptr_t kGpuHeapTable  = 0x705D9B8;
constexpr uintptr_t kImageFrame    = 0x73C9F30;

constexpr int      kImageType     = 9;
constexpr int      kPixelFormat   = 0x14;
constexpr int      kImageFlags    = 4;
constexpr int      kMaxElements   = 0x4E20;
constexpr int      kMaxPictures   = 128;
constexpr size_t   kMaxFileBytes  = 48u * 1024 * 1024;
constexpr uint32_t kMaxFailureLogs = 64;
constexpr uint64_t kImageRetryDelayUs = 5u * 1000 * 1000;

constexpr int kPreviewWidth  = 640;
constexpr int kPreviewHeight = 352;
constexpr int kLoadingWidth  = 960;
constexpr int kLoadingHeight = 544;

constexpr size_t kImageTextureRegs = 168;
constexpr size_t kImageWidth       = 238;
constexpr size_t kImageHeight      = 240;
constexpr size_t kImageStreamed    = 250;
constexpr size_t kImagePixels      = 256;
constexpr size_t kImagePixelBytes  = 272;

constexpr size_t kSlotName     = 304;
constexpr size_t kSlotInUse    = 368;
constexpr size_t kSlotReleased = 384;

constexpr size_t kElementFlags     = 16;
constexpr size_t kElementImage     = 208;
constexpr size_t kElementStreaming = 216;
constexpr size_t kElementUpdate    = 272;

constexpr size_t kStateTop  = 72;
constexpr size_t kStateBase = 80;
constexpr uint32_t kTypeLightUserdata = 2;
constexpr uint32_t kTypeString        = 4;
constexpr uint32_t kTypeUserdata      = 7;

using Hook_t = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t,
                            double, double, double, double, double, double, double, double);
using DynamicImage_t = uint64_t (*)(uint64_t width, uint64_t height, uint64_t format, uint64_t flags, const char* name);
using FindAsset_t = uint64_t (*)(uint64_t type, const char* name, uint64_t errorIfMissing, uint64_t waitMs);

struct Picture
{
    char name[96];
    uint64_t image;
    uint32_t attemptEpoch;
    uint64_t retryAfter;
};

static uintptr_t g_base = 0;
static Picture   g_pictures[kMaxPictures];
static int       g_pictureCount = 0;
static std::atomic<uint32_t> g_pictureEpoch{1};
static std::atomic<uint32_t> g_failureLogs{0};

static Detour g_registerDetour{};
static void*  g_registerOriginal = nullptr;
static Detour g_setImageDetour{};
static void*  g_setImageOriginal = nullptr;

static const uint8_t k_registerImage[] = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x56, 0x53, 0x48, 0x89, 0xFB, 0x45, 0x31,
                                           0xF6, 0x48, 0x8B, 0x73, 0x50, 0x48, 0x3B, 0x73, 0x48 };
static const uint8_t k_setImage[]      = { 0x48, 0x8B, 0x57, 0x48, 0x48, 0x8B, 0x4F, 0x50, 0x31, 0xC0, 0xBE, 0x00,
                                           0x00, 0x00, 0x00, 0x48, 0x39, 0xD1, 0x48, 0x0F, 0x42, 0xF1 };
static const uint8_t k_dynamicImage[]  = { 0x55, 0x48, 0x89, 0xE5, 0x41, 0x57, 0x41, 0x56, 0x41, 0x55, 0x41, 0x54,
                                           0x53, 0x48, 0x81, 0xEC, 0x78, 0x01, 0x00, 0x00, 0x4C, 0x8B, 0x3D };
static const uint8_t k_findAsset[]     = { 0x55, 0x48, 0x89, 0xE5 };

static bool IsMapToken(const char* name)
{
    size_t n = 0;

    for (; name[n]; ++n)
    {
        const char c = name[n];

        if (n >= 56 || !((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
            return false;
    }

    return n > 0;
}

static void LogImageFailure(const char* name, const char* reason, const char* path)
{
    const uint32_t index = g_failureLogs.fetch_add(1, std::memory_order_relaxed);

    if (index < kMaxFailureLogs)
        T7Log_Write("[MapImages] FAILED %s: %s%s%s (will use $white)", name, reason,
                    path ? " | file: " : "", path ? path : "");
    else if (index == kMaxFailureLogs)
        T7Log_Write("[MapImages] further image failure details suppressed after %u entries", kMaxFailureLogs);
}

static uint8_t* ReadWholeFile(const char* path, size_t* outSize, const char** reason)
{
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
    {
        *reason = "file open failed";
        return nullptr;
    }

    // OpenOrbis v0.5.4's struct stat does not match the PS4 kernel layout.
    // In particular, its st_size can truncate valid PNG files before Png_Open.
    // Query the descriptor directly instead of trusting sceKernelStat metadata.
    const off_t fileSize = sceKernelLseek(fd, 0, SEEK_END);

    if (fileSize < 0)
    {
        sceKernelClose(fd);
        *reason = "image file size seek failed";
        return nullptr;
    }

    if (fileSize == 0 || (uint64_t)fileSize > kMaxFileBytes)
    {
        sceKernelClose(fd);
        *reason = "empty or oversized image file";
        return nullptr;
    }

    if (sceKernelLseek(fd, 0, SEEK_SET) != 0)
    {
        sceKernelClose(fd);
        *reason = "image file rewind seek failed";
        return nullptr;
    }

    const size_t size = (size_t)fileSize;
    uint8_t* const data = (uint8_t*)malloc(size);
    size_t done = 0;

    while (data && done < size)
    {
        const int64_t n = sceKernelRead(fd, data + done, size - done);

        if (n <= 0)
            break;

        done += (size_t)n;
    }

    sceKernelClose(fd);

    if (!data || done != size)
    {
        const bool allocated = data != nullptr;
        free(data);
        *reason = allocated ? "incomplete image file read" : "image allocation failed";
        return nullptr;
    }

    *outSize = size;
    return data;
}

static uint8_t* PixelMemory(uint64_t image)
{
    uint64_t pixels = *(uint64_t*)(image + kImagePixels);

    if (pixels == (uint64_t)-1)
        return nullptr;

    const uint64_t heap = *(uint64_t*)(g_base + kGpuHeap);

    if (heap && pixels >= heap && pixels < heap + 16777224)
    {
        const uint64_t index = (*(uint64_t*)(pixels + 8) >> 2) & 0x7FFFFFFF;
        pixels = index ? 16 * index + *(uint64_t*)(*(uint64_t*)(g_base + kGpuHeapTable) + 8) - 16 : 0;
    }

    return (uint8_t*)pixels;
}

static bool WritePicture(const PngImage& picture, uint8_t* pixels, int width, int height, int pitch,
                         const char** reason)
{
    const int64_t across = (int64_t)picture.width * height;
    const int64_t down = (int64_t)picture.height * width;

    memset(pixels, 0, (size_t)pitch * 4 * (size_t)height);

    if (across * 10 >= down * 9 && across * 9 <= down * 10)
        return Png_Scale(picture, pixels, width, height, (size_t)pitch * 4, reason);

    int fitWidth = width;
    int fitHeight = height;

    if (across > down)
        fitHeight = (int)((int64_t)width * picture.height / picture.width);
    else
        fitWidth = (int)((int64_t)height * picture.width / picture.height);

    fitWidth = fitWidth < 1 ? 1 : fitWidth;
    fitHeight = fitHeight < 1 ? 1 : fitHeight;

    const int left = (width - fitWidth) / 2;
    const int top = (height - fitHeight) / 2;

    return Png_Scale(picture, pixels + ((size_t)top * (size_t)pitch + (size_t)left) * 4, fitWidth, fitHeight,
                     (size_t)pitch * 4, reason);
}

static void ReleaseImage(uint64_t image)
{
    *(uint8_t*)(image + kSlotName) = 0;
    *(uint8_t*)(image + kSlotInUse) = 0;
    *(uint64_t*)(image + kSlotReleased) = *(uint64_t*)(g_base + kImageFrame) + 3;
}

static bool ModPicturePath(const char* name, char* path, size_t size)
{
    const char* const key = name + sizeof(T7_MOD_PREVIEW_PREFIX) - 1;

    if (strlen(key) != 8)
        return false;

    uint32_t value = 0;

    for (const char* c = key; *c; ++c)
    {
        const int digit = *c >= '0' && *c <= '9' ? *c - '0' : *c >= 'a' && *c <= 'f' ? *c - 'a' + 10 : -1;

        if (digit < 0)
            return false;

        value = value * 16 + (uint32_t)digit;
    }

    return T7Maps_ModPreviewFile(value, path, size);
}

static uint64_t MakePicture(const char* name)
{
    const bool mod = strncmp(name, T7_MOD_PREVIEW_PREFIX, sizeof(T7_MOD_PREVIEW_PREFIX) - 1) == 0;
    const bool preview = mod || strncmp(name, T7_USERMAP_PREVIEW_PREFIX, sizeof(T7_USERMAP_PREVIEW_PREFIX) - 1) == 0;
    const char* const map = name + (preview ? sizeof(T7_USERMAP_PREVIEW_PREFIX) : sizeof(T7_USERMAP_LOADING_PREFIX)) - 1;
    const int width = preview ? kPreviewWidth : kLoadingWidth;
    const int height = preview ? kPreviewHeight : kLoadingHeight;

    char path[320];

    const bool found = mod ? ModPicturePath(name, path, sizeof(path)) :
                       IsMapToken(map) && (preview ? T7Maps_MapPreviewPath(map, path, sizeof(path)) :
                                                      T7Maps_MapFile(map, "loadingimage.png", path, sizeof(path)));

    if (!found)
    {
        LogImageFailure(name, "preview path not found in package registry", nullptr);
        return 0;
    }

    size_t size = 0;
    const char* reason = nullptr;
    uint8_t* const png = ReadWholeFile(path, &size, &reason);

    if (!png)
    {
        LogImageFailure(name, reason ? reason : "image file read failed", path);
        return 0;
    }

    PngImage picture;

    if (!Png_Open(png, size, &picture, &reason))
    {
        LogImageFailure(name, reason ? reason : "PNG header rejected", path);
        free(png);
        return 0;
    }

    const uint64_t image = ((DynamicImage_t)(g_base + kDynamicImage))((uint64_t)width, (uint64_t)height, kPixelFormat,
                                                                      kImageFlags, name);

    if (!image)
    {
        LogImageFailure(name, "game dynamic image allocation failed", path);
        free(png);
        return 0;
    }

    const uint32_t reg4 = *(uint32_t*)(image + kImageTextureRegs + 16);
    const int pitch = (int)((reg4 >> 13) & 0x3FFF) + 1;
    const uint32_t bytes = *(uint32_t*)(image + kImagePixelBytes);
    uint8_t* const pixels = PixelMemory(image);
    const int imageWidth = *(uint16_t*)(image + kImageWidth);
    const int imageHeight = *(uint16_t*)(image + kImageHeight);

    if (!pixels || imageWidth != width || imageHeight != height || pitch < width || pitch > width + 64 ||
        (uint64_t)pitch * 4 * (uint64_t)height > bytes)
    {
        LogImageFailure(name, !pixels ? "GPU image pixel memory unavailable" :
                        imageWidth != width || imageHeight != height ? "game image dimensions mismatch" :
                        pitch < width || pitch > width + 64 ? "unsupported game image row pitch" :
                        "game image buffer too small", path);
        free(png);
        ReleaseImage(image);
        return 0;
    }

    reason = nullptr;
    const bool written = WritePicture(picture, pixels, width, height, pitch, &reason);
    free(png);

    if (!written)
    {
        LogImageFailure(name, reason ? reason : "PNG decoding or scaling failed", path);
        ReleaseImage(image);
        return 0;
    }

    T7Log_Write("[MapImages] loaded %s (%dx%d) from %s", name, width, height, path);
    return image;
}

static bool IsPictureName(const char* name)
{
    return strncmp(name, T7_USERMAP_PREVIEW_PREFIX, sizeof(T7_USERMAP_PREVIEW_PREFIX) - 1) == 0 ||
           strncmp(name, T7_USERMAP_LOADING_PREFIX, sizeof(T7_USERMAP_LOADING_PREFIX) - 1) == 0 ||
           strncmp(name, T7_MOD_PREVIEW_PREFIX, sizeof(T7_MOD_PREVIEW_PREFIX) - 1) == 0;
}

static bool IsPicture(uint64_t image)
{
    if (!image)
        return false;

    for (int i = 0; i < g_pictureCount; ++i)
    {
        if (g_pictures[i].image == image)
            return true;
    }

    return false;
}

static bool StillOurs(const Picture& picture)
{
    return picture.image && *(uint8_t*)(picture.image + kSlotInUse) &&
           strncmp((const char*)(picture.image + kSlotName), picture.name, 63) == 0;
}

static uint64_t PictureFor(const char* name)
{
    const uint32_t epoch = g_pictureEpoch.load(std::memory_order_relaxed);
    const auto attempt = [epoch](Picture& picture)
    {
        picture.image = MakePicture(picture.name);
        picture.attemptEpoch = epoch;
        picture.retryAfter = picture.image ? 0 : sceKernelGetProcessTime() + kImageRetryDelayUs;
    };

    for (int i = 0; i < g_pictureCount; ++i)
    {
        Picture& picture = g_pictures[i];

        if (strcmp(picture.name, name) != 0)
            continue;

        if (picture.image && !StillOurs(picture))
        {
            attempt(picture);
        }
        else if (!picture.image &&
                 (picture.attemptEpoch != epoch || sceKernelGetProcessTime() >= picture.retryAfter))
        {
            attempt(picture);
        }

        return picture.image;
    }

    if (g_pictureCount >= kMaxPictures || strlen(name) >= sizeof(g_pictures[0].name))
    {
        LogImageFailure(name, "custom image cache full or name too long", nullptr);
        return 0;
    }

    Picture& picture = g_pictures[g_pictureCount];
    snprintf(picture.name, sizeof(picture.name), "%s", name);
    attempt(picture);
    ++g_pictureCount;
    return picture.image;
}

static const char* StringArgument(uint64_t L, int index)
{
    const uint64_t at = *(uint64_t*)(L + kStateBase) + 16 * (uint64_t)index;

    if (at >= *(uint64_t*)(L + kStateTop) || (*(uint32_t*)at & 0xF) != kTypeString)
        return nullptr;

    const uint64_t string = *(uint64_t*)(at + 8);

    if (!string || (*(uint64_t*)(string + 8) & 0x3FFFFFFFFFFFFFFFull) >= 256)
        return nullptr;

    return (const char*)(string + 20);
}

static uint64_t RegisterImage_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                                double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const char* const name = StringArgument(a1, 0);

    if (name && IsPictureName(name))
    {
        uint64_t image = PictureFor(name);

        if (!image)
            image = ((FindAsset_t)(g_base + kFindAsset))(kImageType, "$white", 1, (uint64_t)-1);

        if (image)
        {
            uint64_t top = *(uint64_t*)(a1 + kStateTop);
            *(uint32_t*)top = kTypeLightUserdata;
            *(uint64_t*)(top + 8) = image;
            *(uint64_t*)(a1 + kStateTop) = top + 16;
            return 1;
        }
    }

    return ((Hook_t)g_registerOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uint64_t ElementArgument(uint64_t L)
{
    const uint64_t at = *(uint64_t*)(L + kStateBase);

    if (at >= *(uint64_t*)(L + kStateTop))
        return 0;

    const uint32_t type = *(uint32_t*)at & 0xF;
    uint64_t handle = 0;

    if (type == kTypeLightUserdata)
        handle = *(uint64_t*)(at + 8);
    else if (type == kTypeUserdata && *(uint64_t*)(at + 8))
        handle = *(uint64_t*)(at + 8) + 32;

    if (!handle)
        return 0;

    const uint16_t index = *(uint16_t*)handle;

    if (index >= kMaxElements || *(uint16_t*)(handle + 2) != *(uint16_t*)(g_base + kGenerations + 2 * (uint64_t)index))
        return 0;

    return g_base + kElements + 408 * (uint64_t)index;
}

static uint64_t SetImage_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t at = *(uint64_t*)(a1 + kStateBase) + 16;

    if (at < *(uint64_t*)(a1 + kStateTop) && (*(uint32_t*)at & 0xF) == kTypeLightUserdata &&
        IsPicture(*(uint64_t*)(at + 8)))
    {
        const uint64_t image = *(uint64_t*)(at + 8);
        const uint64_t element = ElementArgument(a1);

        if (element && !*(uint8_t*)(image + kImageStreamed))
        {
            *(uint64_t*)(element + kElementImage) = image;
            *(uint8_t*)(element + kElementFlags) &= (uint8_t)~2;

            if (*(uint64_t*)(element + kElementUpdate) == g_base + kStreamedDraw)
                *(uint16_t*)(element + kElementStreaming) = 255;
        }

        return 0;
    }

    return ((Hook_t)g_setImageOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static bool Matches(uintptr_t base, uintptr_t offset, const uint8_t* bytes, size_t size)
{
    const uintptr_t at = base + offset;
    return RangeReadable(at, size) && memcmp((const void*)at, bytes, size) == 0;
}
}

void T7MapImages_RefreshMisses()
{
    // Existing GPU images remain live. Only failed image registrations are
    // retried when the menu next asks for them after a map rescan.
    T7MapImages::g_pictureEpoch.fetch_add(1, std::memory_order_relaxed);
}

void T7MapImages_Install(uintptr_t base)
{
    using namespace T7MapImages;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;

    if (!Matches(base, kRegisterImage, k_registerImage, sizeof(k_registerImage)))
    {
        T7Log_Write("[MapImages] DISABLED: register-image signature mismatch");
        return;
    }

    if (!Matches(base, kSetImage, k_setImage, sizeof(k_setImage)))
    {
        T7Log_Write("[MapImages] DISABLED: set-image signature mismatch");
        return;
    }

    if (!Matches(base, kDynamicImage, k_dynamicImage, sizeof(k_dynamicImage)))
    {
        T7Log_Write("[MapImages] DISABLED: dynamic-image signature mismatch");
        return;
    }

    if (!Matches(base, kFindAsset, k_findAsset, sizeof(k_findAsset)))
    {
        T7Log_Write("[MapImages] DISABLED: find-asset signature mismatch");
        return;
    }

    g_base = base;

    Detour_Attach(&g_setImageDetour, (uint64_t)(base + kSetImage), (void*)SetImage_h, &g_setImageOriginal);

    if (!g_setImageOriginal)
    {
        T7Log_Write("[MapImages] DISABLED: set-image detour failed");
        return;
    }

    Detour_Attach(&g_registerDetour, (uint64_t)(base + kRegisterImage), (void*)RegisterImage_h, &g_registerOriginal);

    if (!g_registerOriginal)
    {
        T7Log_Write("[MapImages] DISABLED: register-image detour failed");
        return;
    }

    T7Log_Write("[MapImages] hooks installed; custom preview loading enabled");
}
