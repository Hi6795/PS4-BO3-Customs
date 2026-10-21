#include "headers.hpp"
#include "t7_share.hpp"

#include <net.h>
#include <atomic>

namespace T7Share
{
constexpr uint16_t  kPort               = 28330;
constexpr uintptr_t kNetworkMode        = 0xA6DBA98;
constexpr uintptr_t kClientSessions     = 0xA605850;
constexpr size_t    kClientSessionSize  = 11272;
constexpr size_t    kSessionActive      = 64;
constexpr size_t    kSessionHostAddress = 136;
constexpr size_t    kSessionHostType    = 144;
constexpr size_t    kSessionHostBlob    = 153;
constexpr size_t    kSessionHostHeard   = 9076;
constexpr uintptr_t kHostSessions       = 0xA60C520;
constexpr size_t    kHostSessionSize    = 416720;
constexpr uintptr_t kCanMigrate         = 0xD8F880;
constexpr uintptr_t kMigrateTo          = 0xD8FE80;
constexpr uintptr_t kMigrateMessage     = 0xD98F20;
constexpr uintptr_t kCanHost            = 0xCC2640;
constexpr uintptr_t kCanBecomeHost      = 0xD8FA50;
constexpr int32_t   kSessionRunning     = 2;
constexpr uint64_t  kMigrateWindowUs    = 30000000;
constexpr size_t    kClientMigrateState = 11208;
constexpr size_t    kSessionMembers     = 248;
constexpr size_t    kMemberStride       = 48;
constexpr int       kMemberSlots        = 18;
constexpr size_t    kMemberLobby        = 1240;
constexpr size_t    kMemberLobbyStride  = 32;
constexpr size_t    kMemberAddress      = 4;
constexpr size_t    kMemberNetsrc       = 12;
constexpr int32_t   kNetsrcNoAddress    = 1;
constexpr uintptr_t kLocalUsers         = 0x1612960;
constexpr uintptr_t kMigrateWaitDvar    = 0xA6DBD68;
constexpr size_t    kDvarKind           = 0x14;
constexpr size_t    kDvarInt            = 0x20;
constexpr int32_t   kMigrateWaitMs      = 8000;
constexpr size_t    kClientMigrateTo    = 11064;
constexpr uintptr_t kPrintInfo          = 0xE7EC90;
constexpr uint64_t  kLobbyLogUs         = 60000000;
constexpr int       kLobbyLogLines      = 400;
constexpr uintptr_t kTaskList           = 0xCD68BF0;
constexpr uintptr_t kLaunchTask         = 0x15D70B0;
constexpr size_t    kTaskDefinition     = 8;
constexpr size_t    kTaskTimeout        = 44;
constexpr int       kTaskWalk           = 512;
constexpr size_t    kHostBlobSize       = 37;
constexpr size_t    kHostSlotSize       = 6;
constexpr int       kMaxHosts           = 3;
constexpr size_t    kGameLobby          = 1;
constexpr int32_t   kNetworkLan         = 1;
constexpr int32_t   kAddressLoopback    = 2;
constexpr int32_t   kAddressIp          = 4;

constexpr int      kMaxEntries          = 768;
constexpr size_t   kRelMax              = 160;
constexpr size_t   kPathMax             = kT7SharePath + kT7ShareName + kRelMax + 32;
constexpr size_t   kChunk               = 256 * 1024;
constexpr int      kMaxConnections      = 4;
constexpr int      kWalkDepth           = 6;
constexpr int      kSocketWaitUs        = 2000000;
constexpr int      kSocketBuffers[]     = { 1024 * 1024, 512 * 1024, 256 * 1024 };
constexpr int      kQuietRounds         = 10;
constexpr uint64_t kConnectWaitUs       = 2500000;
constexpr uint64_t kConnectManyUs       = 1500000;
constexpr uint64_t kConnectStepUs       = 20000;
constexpr uint64_t kDeafHostUs          = 600000000;
constexpr uint64_t kSnapshotUs          = 250000;
constexpr uint64_t kWrapEveryUs         = 1000000;
constexpr uint64_t kFailureShownUs      = 6000000;
constexpr uint64_t kAwayUs              = 3000000;
constexpr uint64_t kSettleUs            = 400000;
constexpr uint64_t kLaunchGraceUs       = 90000000;
constexpr uint64_t kFreshListUs         = 500000;
constexpr int      kListTries           = 6;
constexpr uint64_t kListRetryUs         = 500000;

constexpr uint32_t kUdpMagic               = 0x55433342;
constexpr uint8_t  kUdpVersion             = 2;
constexpr size_t   kUdpChunk               = 960;
constexpr int      kUdpRing                = 8192;
constexpr double   kUdpWindowStart         = 64.0;
constexpr double   kUdpWindowMax           = 4096.0;
constexpr double   kUdpWindowMin           = 16.0;
constexpr int      kUdpInbox               = 512;
constexpr int      kUdpPeers               = 4;
constexpr size_t   kUdpCache               = 512 * 1024;
constexpr size_t   kUdpListMax             = 256 * 1024;
constexpr uint64_t kUdpReachUs             = 8000000;
constexpr uint64_t kUdpProbeEveryUs        = 400000;
constexpr uint64_t kUdpQuietUs             = 15000000;
constexpr uint64_t kUdpScanUs              = 10000;
constexpr int      kUdpBatch               = 8;
constexpr int      kUdpLowSlots            = 32;
constexpr uint64_t kUdpLowSpanS            = 30;
constexpr uint64_t kUdpReportUs            = 5000000;
constexpr uint64_t kUdpRtoMinUs            = 150000;
constexpr uint64_t kUdpRtoMaxUs            = 3000000;
constexpr uint64_t kUdpPeerIdleUs          = 60000000;
constexpr uint64_t kUdpSettleUs            = 300000;
constexpr int32_t  kUdpStatsObject         = -2;
constexpr uint64_t kUdpStatsEveryUs        = 1000000;
constexpr double   kRateSmoothUs           = 2000000.0;
constexpr uint64_t kMapsEveryUs            = 3000000;
constexpr uint64_t kMapsStaleUs            = 12000000;
constexpr int      kMapsMembers            = 18;
constexpr int      kMapsMax                = 512;
constexpr int      kMapsPerPage            = (int)(kUdpChunk / sizeof(uint32_t));
constexpr int      kMapsPages              = (kMapsMax + kMapsPerPage - 1) / kMapsPerPage;
constexpr uint64_t kProgressEveryUs        = 1000000;
constexpr uint64_t kProgressStaleUs        = 5000000;
constexpr int      kUdpSendTries           = 50;
constexpr unsigned kUdpSendWaitUs          = 200;
constexpr int      kUdpAsks                = 1024;
constexpr int      kUdpRoutes              = 8;
constexpr uintptr_t kDwReceive             = 0x40CA60;
constexpr uint64_t kNoticeDelayUs          = 1500000;
constexpr uint64_t kNoticeRepeatUs         = 300000000;
constexpr size_t   kPage                   = 0x4000;
constexpr size_t   kMemoryReserve          = 1024 * 1024;
constexpr int      kMapAnonymous           = 0x1000 | 0x2;

enum Phase
{
    kIdle,
    kContacting,
    kListing,
    kDownloading,
    kInstalling,
    kReady,
    kFailed,
};

struct Role
{
    bool     lan;
    bool     host;
    bool     client;
    uint32_t hostIp;
    int      hostCount;
    uint32_t hosts[kMaxHosts];
    uint16_t ports[kMaxHosts];
};

struct Hosted
{
    bool serving;
    bool custom;
    char map[kT7ShareName];
    char mapRoot[kT7SharePath];
    int  modCount;
    char mods[kT7ShareMods][kT7ShareName];
    char modRoots[kT7ShareMods][kT7SharePath];
};

struct Served
{
    uint64_t size;
    int32_t  kind;
    char     rel[kRelMax];
    char     path[kT7SharePath + kRelMax + 2];
};

struct Entry
{
    uint64_t size;
    int32_t  kind;
    bool     wanted;
    char     rel[kRelMax];
};

struct Session
{
    bool     active;
    bool     needMap;
    bool     listed;
    bool     fetched;
    uint32_t hostIp;
    uint32_t reached;
    int      hostCount;
    uint32_t hosts[kMaxHosts];
    uint16_t ports[kMaxHosts];
    int      modCount;
    int      newMods;
    char     map[kT7ShareName];
    char     mods[kT7ShareMods][kT7ShareName];
    char     current[kRelMax];
    char     error[160];
};

struct Stream
{
    SceNetId                 socket;
    const std::atomic<bool>* stop;
    int                      have;
    int                      at;
    char                     held[4096];
};

enum UdpType : uint8_t
{
    kUdpAsk = 1,
    kUdpPiece = 2,
    kUdpRefuse = 3,
    kUdpStats = 4,
    kUdpMaps = 5,
    kUdpLacks = 6,
    kUdpProgress = 7,
    kUdpProgressList = 8,
};

enum UdpRefusal : uint64_t
{
    kRefuseNotSharing = 1,
    kRefuseNoFile = 2,
    kRefuseChanged = 3,
};

struct UdpHeader
{
    uint32_t magic;
    uint8_t  version;
    uint8_t  type;
    uint16_t size;
    uint32_t token;
    int32_t  object;
    uint64_t offset;
    uint64_t total;
};

static_assert(sizeof(UdpHeader) == 32, "the share header is part of the protocol");

constexpr size_t kUdpPacket = sizeof(UdpHeader) + kUdpChunk;

struct Arrival
{
    uint32_t from;
    int      size;
    uint8_t  bytes[kUdpPacket];
};

struct Piece
{
    uint8_t  state;
    uint8_t  tries;
    uint64_t askedAt;
};

struct Slot
{
    uint64_t at;
    uint32_t size;
};

struct UdpPeer
{
    uint32_t token;
    uint32_t address;
    uint16_t port;
    int      socket;
    uint64_t usedAt;
    uint64_t began;
    uint64_t sent;
    uint64_t statsAt;
    uint64_t statsSent;
    uint64_t upload;
    int      count;
    Served*  files;
    char*    list;
    size_t   listSize;
    int      fd;
    int      fdId;
    uint64_t cacheAt;
    size_t   cacheSize;
    char*    cache;
};

struct Ask
{
    int       socket;
    uint32_t  address;
    uint16_t  port;
    UdpHeader header;
};

struct Route
{
    uint32_t address;
    uint16_t port;
    int      socket;
    uint64_t at;
};

struct Link
{
    Stream*  stream;
    int      socket;
    uint16_t port;
    uint32_t address;
    uint32_t token;
    Piece*   pieces;
    Slot*    slots;
    char*    list;
    size_t   listSize;
    size_t   listAt;
    uint64_t resent;
    double   window;
    bool     starting;
    uint64_t smoothed;
    uint64_t fastest;
    uint64_t timeout;
    uint64_t lows[kUdpLowSlots];
    uint64_t lowAt[kUdpLowSlots];
    uint64_t reportAt;
    uint64_t reportBytes;
    uint8_t  version;
};

struct ProgressEntry
{
    uint64_t key;
    int32_t  percent;
    uint32_t map;
};

struct MapsMember
{
    uint64_t xuid;
    uint64_t seenAt;
    uint64_t progressAt;
    uint32_t progressMap;
    int      progress;
    uint32_t seq;
    uint32_t pagesGot;
    int      pendingTotal;
    int      count;
    bool     known;
    uint32_t pending[kMapsMax];
    uint32_t hashes[kMapsMax];
};

struct Spin
{
    std::atomic<bool>& flag;

    explicit Spin(std::atomic<bool>& taken) : flag(taken)
    {
        while (flag.exchange(true, std::memory_order_acquire))
            sceKernelUsleep(50);
    }

    ~Spin()
    {
        flag.store(false, std::memory_order_release);
    }
};

static uintptr_t g_base = 0;
static bool      g_ready = false;

static Hosted            g_hosted{};
static std::atomic<bool> g_hostedLock{false};
static std::atomic<bool> g_serve{false};
static std::atomic<int>  g_connections{0};
static std::atomic<bool> g_hostedWanted{false};
static std::atomic<uint32_t> g_hostedVersion{0};
static uint64_t          g_hostedAt = 0;

static Session               g_session{};
static std::atomic<bool>     g_textLock{false};
static std::atomic<bool>     g_evaluating{false};
static std::atomic<bool>     g_lastHold{false};
static std::atomic<int>      g_phase{kIdle};
static std::atomic<bool>     g_busy{false};
static std::atomic<bool>     g_stop{false};
static std::atomic<uint64_t> g_total{0};
static std::atomic<uint64_t> g_done{0};
static std::atomic<uint64_t> g_received{0};
static std::atomic<uint64_t> g_writeBytes{0};
static std::atomic<uint64_t> g_writeUs{0};
static std::atomic<uint64_t> g_hostRead{0};
static std::atomic<uint64_t> g_hostUpload{0};
static MapsMember            g_mapsMembers[kMapsMembers];
static std::atomic<bool>     g_mapsLock{false};
static std::atomic<uint32_t> g_mapsVersion{1};
static std::atomic<uint64_t> g_myXuid{0};
static std::atomic<bool>     g_reportLock{false};
static std::atomic<bool>     g_lacksLock{false};
static std::atomic<bool>     g_banLock{false};
static uint64_t              g_bans[2 * kMapsMembers];
static int                   g_banCount = 0;
static uint32_t              g_lacksMap = 0;
static uint64_t              g_lacksAt = 0;
static int                   g_lacksCount = 0;
static uint64_t              g_lacksKeys[kMapsMembers];
static ProgressEntry         g_progress[kMapsMembers];
static int                   g_progressCount = 0;
static uint64_t              g_progressAt = 0;
static uint32_t              g_reportHosts[kMaxHosts];
static int                   g_reportHostCount = 0;
static std::atomic<uint64_t> g_readRate{0};
static std::atomic<int>      g_fileAt{0};
static std::atomic<int>      g_fileCount{0};
static std::atomic<bool>     g_wrapped{false};
static std::atomic<bool>     g_mapsChanged{false};
static std::atomic<uint64_t> g_failedAt{0};
static uint64_t              g_wrapTriedAt = 0;
static uint64_t              g_awaySince = 0;
static char                  g_pendingMap[kT7ShareName] = "";
static uint32_t              g_pendingHost = 0;
static uint64_t              g_pendingSince = 0;
static uint64_t              g_heldAt = 0;
static uint64_t              g_keptAt = 0;
static uint64_t              g_keptRest = 0;
static std::atomic<uint32_t> g_deafHost{0};
static std::atomic<uint64_t> g_deafSince{0};
static bool                  g_overriding = false;
static uint32_t              g_loggedHost = 0;

static bool                  g_udpReady = false;
static Detour                g_dwReceiveDetour{};
static int (*g_dwReceive)(int64_t, uint32_t*, uint16_t*, void*, uint32_t) = nullptr;

using EngineCall = uint64_t (*)(uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, uint64_t, double, double, double, double,
                                double, double, double, double);

static Detour                g_canMigrateDetour{};
static Detour                g_migrateMessageDetour{};
static EngineCall            g_canMigrate = nullptr;
static EngineCall            g_migrateMessage = nullptr;
static Detour                g_canHostDetour{};
static Detour                g_canBecomeHostDetour{};
static EngineCall            g_canHost = nullptr;
static EngineCall            g_canBecomeHost = nullptr;
static std::atomic<uint64_t> g_canHostLogged{0};
static std::atomic<int>      g_becomeHostLogged{-1};
static std::atomic<int32_t>  g_savedMigrateWait{-1};
static std::atomic<uint64_t> g_migrateUntil{0};
static std::atomic<uint64_t> g_migrateLogged{0};
#ifdef _DEBUG
static Detour                g_printInfoDetour{};
static EngineCall            g_printInfo = nullptr;
#endif
static std::atomic<uint64_t> g_lobbyLogUntil{0};
static std::atomic<int>      g_lobbyLogLines{0};
static Ask*                  g_asks = nullptr;
static int                   g_askHead = 0;
static int                   g_askCount = 0;
static std::atomic<bool>     g_askLock{false};
static Route                 g_routes[kUdpRoutes];
static std::atomic<bool>     g_routeLock{false};
static char                  g_noticeMap[kT7ShareName] = "";
static char                  g_noticeText[160] = "";
static uint32_t              g_noticeHost = 0;
static std::atomic<uint64_t> g_noticeAt{0};
static char                  g_noticedMap[kT7ShareName] = "";
static uint32_t              g_noticedHost = 0;
static uint64_t              g_noticedAt = 0;
static Arrival*              g_inbox = nullptr;
static int                   g_inboxHead = 0;
static int                   g_inboxCount = 0;
static std::atomic<bool>     g_inboxLock{false};
static std::atomic<uint32_t> g_fetchToken{0};
static UdpPeer               g_peers[kUdpPeers];

static size_t FreeMemory()
{
    size_t free = 0;
    return sceKernelAvailableFlexibleMemorySize(&free) == 0 ? free : (size_t)-1;
}

static void* Take(size_t size)
{
    void* address = nullptr;
    const size_t rounded = (size + kPage - 1) & ~(kPage - 1);
    const size_t free = FreeMemory();

    if (free < rounded + kMemoryReserve)
    {
        T7Log_Write("[Share] not enough free memory for %llu KB (%llu KB free)", (unsigned long long)(rounded / 1024),
                    (unsigned long long)(free / 1024));
        return nullptr;
    }

    if (sceKernelMmap(nullptr, rounded, PROT_READ | PROT_WRITE, kMapAnonymous, -1, 0, &address) < 0)
        return nullptr;

    return address;
}

static void Give(void* address, size_t size)
{
    if (address)
        sceKernelMunmap(address, (size + kPage - 1) & ~(kPage - 1));
}

static bool EqualsNoCase(const char* a, const char* b)
{
    for (;; ++a, ++b)
    {
        const char x = (*a >= 'A' && *a <= 'Z') ? (char)(*a + 32) : *a;
        const char y = (*b >= 'A' && *b <= 'Z') ? (char)(*b + 32) : *b;

        if (x != y)
            return false;

        if (!x)
            return true;
    }
}

static bool EndsWith(const char* text, const char* tail)
{
    const size_t n = strlen(text);
    const size_t m = strlen(tail);
    return n >= m && strcmp(text + n - m, tail) == 0;
}

static bool Private(uint32_t address)
{
    const uint8_t* const b = (const uint8_t*)&address;
    return b[0] == 10 || (b[0] == 172 && (b[1] & 0xF0) == 16) || (b[0] == 192 && b[1] == 168) ||
           (b[0] == 169 && b[1] == 254);
}

static bool SafeName(const char* name, size_t length)
{
    if (!length || name[0] == '.')
        return false;

    for (size_t i = 0; i < length; ++i)
    {
        const char c = name[i];

        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' ||
              c == '-'))
            return false;
    }

    return true;
}

static bool SafeRel(const char* rel)
{
    if (strlen(rel) >= kRelMax)
        return false;

    int parts = 0;

    for (const char* at = rel;;)
    {
        const char* end = at;

        while (*end && *end != '/')
            ++end;

        if (!SafeName(at, (size_t)(end - at)) || ++parts > kWalkDepth + 1)
            return false;

        if (!*end)
            return true;

        at = end + 1;
    }
}

static bool SafeFolder(const char* name)
{
    size_t n = 0;

    for (; name[n]; ++n)
    {
        const unsigned char c = (unsigned char)name[n];

        if (n >= kT7ShareName - 1 || c < 0x20 || c >= 0x7F || c == '"' || c == '\\' || c == '/' || c == ':' ||
            c == '*' || c == '?')
            return false;
    }

    return n > 0 && name[0] != '.' && name[0] != ' ' && name[n - 1] != ' ';
}

static bool ReadMapName(char* out, size_t size)
{
    if (!T7Lua_DvarString("ui_mapname", out, size) || !out[0] || strlen(out) >= kT7ShareName)
    {
        out[0] = 0;
        return false;
    }

    for (char* c = out; *c; ++c)
    {
        if (*c >= 'A' && *c <= 'Z')
            *c = (char)(*c + 32);

        if (!((*c >= 'a' && *c <= 'z') || (*c >= '0' && *c <= '9') || *c == '_'))
        {
            out[0] = 0;
            return false;
        }
    }

    return true;
}

static bool Usable(uint32_t address)
{
    const uint8_t* const b = (const uint8_t*)&address;
    return Private(address) && b[3] != 0 && b[3] != 255;
}

static int HostAddresses(const uint8_t* blob, uint32_t* out, uint16_t* ports)
{
    int count = 0;

    for (int pass = 0; pass < 2 && count == 0; ++pass)
    {
        const size_t step = pass == 0 ? kHostSlotSize : 1;

        for (size_t at = 0; at + sizeof(uint32_t) <= kHostBlobSize && count < kMaxHosts; at += step)
        {
            uint32_t address;
            memcpy(&address, blob + at, sizeof(address));

            bool known = !Usable(address);

            for (int i = 0; i < count && !known; ++i)
                known = out[i] == address;

            if (!known)
            {
                ports[count] = pass == 0 ? (uint16_t)(blob[at + 4] | (blob[at + 5] << 8)) : 0;
                out[count++] = address;
            }
        }
    }

    return count;
}

static void LogHost(int32_t type, const uint8_t* handle, const uint8_t* blob, const Role& role)
{
    char raw[kHostBlobSize * 2 + 1];
    char found[kMaxHosts * 16 + 8] = "none";
    size_t used = 0;

    for (size_t i = 0; i < kHostBlobSize; ++i)
        snprintf(raw + i * 2, 3, "%02X", blob[i]);

    for (int i = 0; i < role.hostCount; ++i)
    {
        const uint8_t* const b = (const uint8_t*)&role.hosts[i];
        used += (size_t)snprintf(found + used, sizeof(found) - used, "%s%u.%u.%u.%u", i ? ", " : "", b[0], b[1], b[2],
                                 b[3]);
    }

    T7Log_Write("[Share] LAN game lobby: host address type %d, handle %u.%u.%u.%u, LAN address %s, raw %s", type,
                handle[0], handle[1], handle[2], handle[3], found, raw);
}

static Role ReadRole()
{
    Role role{};

    if (!g_ready)
        return role;

    role.lan = *(const int32_t*)(g_base + kNetworkMode) == kNetworkLan;

    const uintptr_t session = g_base + kClientSessions + kClientSessionSize * kGameLobby;

    if (!role.lan || *(const int32_t*)(session + kSessionActive) <= 0)
    {
        g_loggedHost = 0;
        return role;
    }

    const int32_t type = *(const int32_t*)(session + kSessionHostType);
    const uint8_t* const blob = (const uint8_t*)(session + kSessionHostBlob);

    role.host = type == kAddressLoopback;

    if (type == kAddressIp)
    {
        role.hostCount = HostAddresses(blob, role.hosts, role.ports);
        role.hostIp = role.hostCount > 0 ? role.hosts[0] : 0;
        role.client = role.hostCount > 0;
    }

    uint32_t mark = 2166136261u ^ (uint32_t)type;

    for (size_t i = 0; i < kHostBlobSize; ++i)
        mark = (mark ^ blob[i]) * 16777619u;

    if (mark != g_loggedHost)
    {
        g_loggedHost = mark;
        LogHost(type, (const uint8_t*)(session + kSessionHostAddress), blob, role);
    }

    return role;
}

static void SetWait(SceNetId socket, int microseconds)
{
    sceNetSetsockopt(socket, SCE_NET_SOL_SOCKET, SCE_NET_SO_RCVTIMEO, &microseconds, sizeof(microseconds));
    sceNetSetsockopt(socket, SCE_NET_SOL_SOCKET, SCE_NET_SO_SNDTIMEO, &microseconds, sizeof(microseconds));
}

static void SetBlocking(SceNetId socket, bool blocking)
{
    int value = blocking ? 0 : 1;
    sceNetSetsockopt(socket, SCE_NET_SOL_SOCKET, SCE_NET_SO_NBIO, &value, sizeof(value));
}

static void Tune(SceNetId socket)
{
    int on = 1;
    sceNetSetsockopt(socket, SCE_NET_IPPROTO_TCP, SCE_NET_TCP_NODELAY, &on, sizeof(on));

    const int options[] = { SCE_NET_SO_SNDBUF, SCE_NET_SO_RCVBUF };

    for (const int option : options)
    {
        for (int size : kSocketBuffers)
        {
            if (sceNetSetsockopt(socket, SCE_NET_SOL_SOCKET, option, &size, sizeof(size)) >= 0)
                break;
        }
    }
}

static bool Waited(int result)
{
    return result == SCE_NET_ERROR_EWOULDBLOCK || result == SCE_NET_ERROR_ETIMEDOUT || result == SCE_NET_ERROR_EINTR;
}

static int Receive(Stream& stream, void* out, int size)
{
    for (int quiet = 0; quiet < kQuietRounds;)
    {
        if (stream.stop && stream.stop->load())
            return -1;

        const int got = sceNetRecv(stream.socket, out, (size_t)size, 0);

        if (got >= 0)
            return got;

        if (!Waited(got))
            return -1;

        ++quiet;
    }

    return -1;
}

static bool ReadLine(Stream& stream, char* out, size_t size)
{
    size_t used = 0;

    for (;;)
    {
        if (stream.at >= stream.have)
        {
            const int got = Receive(stream, stream.held, (int)sizeof(stream.held));

            if (got <= 0)
                return false;

            stream.have = got;
            stream.at = 0;
        }

        const char c = stream.held[stream.at++];

        if (c == '\n')
        {
            out[used] = 0;
            return true;
        }

        if (c == '\r')
            continue;

        if (used + 1 >= size)
            return false;

        out[used++] = c;
    }
}

static int ReadBody(Stream& stream, void* out, int size)
{
    if (stream.at < stream.have)
    {
        const int held = stream.have - stream.at;
        const int taken = held < size ? held : size;
        memcpy(out, stream.held + stream.at, (size_t)taken);
        stream.at += taken;
        return taken;
    }

    return Receive(stream, out, size);
}

static bool SendAll(Stream& stream, const void* data, size_t size)
{
    const char* at = (const char*)data;

    for (int quiet = 0; size > 0;)
    {
        if (stream.stop && stream.stop->load())
            return false;

        const int sent = sceNetSend(stream.socket, at, size, 0);

        if (sent > 0)
        {
            at += sent;
            size -= (size_t)sent;
            quiet = 0;
            continue;
        }

        if (!Waited(sent) || ++quiet >= kQuietRounds)
            return false;
    }

    return true;
}

static bool SendText(Stream& stream, const char* format, ...)
{
    char text[512];
    va_list args;
    va_start(args, format);
    const int n = vsnprintf(text, sizeof(text), format, args);
    va_end(args);

    return n > 0 && (size_t)n < sizeof(text) && SendAll(stream, text, (size_t)n);
}

static void Walk(const char* root, const char* rel, int depth, int kind, Served* out, int* count)
{
    char folder[kT7SharePath + kRelMax + 2];

    if (rel[0])
        snprintf(folder, sizeof(folder), "%s/%s", root, rel);
    else
        snprintf(folder, sizeof(folder), "%s", root);

    const int fd = sceKernelOpen(folder, SCE_KERNEL_O_RDONLY | SCE_KERNEL_O_DIRECTORY, 0);

    if (fd < 0)
        return;

    constexpr int kListing = 0x8000;
    char* const listing = (char*)Take(kListing);

    while (listing)
    {
        const int n = sceKernelGetdents(fd, listing, kListing);

        if (n <= 0)
            break;

        for (int at = 0; at + 8 <= n;)
        {
            const SceKernelDirent* const entry = (const SceKernelDirent*)(listing + at);

            if (entry->d_reclen == 0)
                break;

            at += entry->d_reclen;

            if (entry->d_fileno == 0 || !SafeName(entry->d_name, strlen(entry->d_name)))
                continue;

            char child[kRelMax];
            const int length = rel[0] ? snprintf(child, sizeof(child), "%s/%s", rel, entry->d_name)
                                      : snprintf(child, sizeof(child), "%s", entry->d_name);

            if (length <= 0 || (size_t)length >= sizeof(child))
                continue;

            if (entry->d_type == SCE_KERNEL_DT_DIR)
            {
                if (depth < kWalkDepth)
                    Walk(root, child, depth + 1, kind, out, count);

                continue;
            }

            if (EndsWith(child, ".part") || *count >= kMaxEntries)
                continue;

            Served& file = out[*count];
            SceKernelStat st;
            snprintf(file.path, sizeof(file.path), "%s/%s", root, child);

            if (sceKernelStat(file.path, &st) != 0 || SCE_KERNEL_S_ISDIR(st.st_mode) || st.st_size < 0)
                continue;

            file.size = (uint64_t)st.st_size;
            file.kind = kind;
            snprintf(file.rel, sizeof(file.rel), "%s", child);
            ++*count;
        }
    }

    Give(listing, kListing);
    sceKernelClose(fd);
}

static int64_t TimedRead(int fd, void* data, size_t size, int64_t offset)
{
    const uint64_t began = sceKernelGetProcessTime();
    const int64_t got = sceKernelPread(fd, data, size, (off_t)offset);
    const uint64_t spent = sceKernelGetProcessTime() - began;

    if (got > 0 && spent > 0)
    {
        const uint64_t sample = (uint64_t)got * 1000000 / spent;
        const uint64_t was = g_readRate.load(std::memory_order_relaxed);
        g_readRate.store(was ? (was * 7 + sample) / 8 : sample, std::memory_order_relaxed);
    }

    return got;
}

static bool SendFile(Stream& stream, const Served& file, uint64_t offset, char* buffer, uint64_t* total, uint64_t upload)
{
    const int fd = sceKernelOpen(file.path, SCE_KERNEL_O_RDONLY, 0);
    SceKernelStat st;

    if (fd < 0 || sceKernelFstat(fd, &st) != 0 || (uint64_t)st.st_size != file.size || offset > file.size)
    {
        if (fd >= 0)
            sceKernelClose(fd);

        SendText(stream, "ERR that file changed\n");
        return false;
    }

    bool sent = SendText(stream, "DATA %llu %llu %llu\n", (unsigned long long)(file.size - offset),
                         (unsigned long long)g_readRate.load(std::memory_order_relaxed), (unsigned long long)upload);

    while (sent && offset < file.size)
    {
        const uint64_t left = file.size - offset;
        const size_t want = left < kChunk ? (size_t)left : kChunk;
        const int64_t got = TimedRead(fd, buffer, want, (int64_t)offset);

        if (got <= 0 || !SendAll(stream, buffer, (size_t)got))
        {
            sent = false;
            break;
        }

        offset += (uint64_t)got;
        *total += (uint64_t)got;
    }

    sceKernelClose(fd);
    return sent;
}

static void Answer(Stream& stream, Served* files, char* buffer)
{
    const uint32_t version = g_hostedVersion.load();
    g_hostedWanted.store(true);

    for (uint64_t waited = 0; waited < kFreshListUs && g_hostedVersion.load() == version; waited += 10000)
        sceKernelUsleep(10000);

    Hosted hosted;

    {
        Spin lock(g_hostedLock);
        hosted = g_hosted;
    }

    if (!g_serve.load() || !hosted.serving)
    {
        SendText(stream, "BO3C 1 NO\n");
        return;
    }

    int count = 0;

    if (hosted.custom)
        Walk(hosted.mapRoot, "", 0, -1, files, &count);

    for (int k = 0; k < hosted.modCount; ++k)
        Walk(hosted.modRoots[k], "", 0, k, files, &count);

    bool sent = SendText(stream, "BO3C 1 OK\n") &&
                SendText(stream, "map %s %d\n", hosted.map[0] ? hosted.map : "-", hosted.custom ? 1 : 0);

    for (int k = 0; sent && k < hosted.modCount; ++k)
        sent = SendText(stream, "mod %s\n", hosted.mods[k]);

    for (int i = 0; sent && i < count; ++i)
        sent = SendText(stream, "file %d %d %llu %s\n", i, files[i].kind, (unsigned long long)files[i].size,
                        files[i].rel);

    sent = sent && SendText(stream, "end\n");

    T7Log_Write("[Share] sent the list for %s: %d file(s), %d mod(s)", hosted.map, count, hosted.modCount);

    char line[256];
    uint64_t total = 0;
    int asked = 0;
    const uint64_t began = sceKernelGetProcessTime();

    while (sent && ReadLine(stream, line, sizeof(line)))
    {
        if (strncmp(line, "GET ", 4) != 0)
            break;

        ++asked;

        char* at = line + 4;
        const long id = strtol(at, &at, 10);
        const uint64_t offset = strtoull(at, &at, 10);

        if (!g_serve.load())
        {
            SendText(stream, "ERR the host stopped sharing\n");
            break;
        }

        if (id < 0 || id >= count)
        {
            SendText(stream, "ERR no such file\n");
            break;
        }

        const uint64_t spent = sceKernelGetProcessTime() - began;
        sent = SendFile(stream, files[id], offset, buffer, &total, spent ? total * 1000000 / spent : 0);
    }

    if (asked > 0)
    {
        const double seconds = (double)(sceKernelGetProcessTime() - began) / 1000000.0;
        T7Log_Write("[Share] sent %d file(s), %llu bytes in %.1f s (%.2f MB/s)%s", asked, (unsigned long long)total, seconds,
                    seconds > 0.0 ? (double)total / seconds / 1048576.0 : 0.0, sent ? "" : ", then the link dropped");
    }
}

static void* Serve(void* argument)
{
    Stream* const stream = (Stream*)Take(sizeof(Stream));
    Served* const files = (Served*)Take(sizeof(Served) * kMaxEntries);
    char* const buffer = (char*)Take(kChunk);
    const SceNetId socket = (SceNetId)(intptr_t)argument;

    if (stream && files && buffer)
    {
        memset(stream, 0, sizeof(*stream));
        stream->socket = socket;

        char line[64];

        if (ReadLine(*stream, line, sizeof(line)) && strcmp(line, "BO3C 1 LIST") == 0)
            Answer(*stream, files, buffer);
    }

    Give(stream, sizeof(Stream));
    Give(files, sizeof(Served) * kMaxEntries);
    Give(buffer, kChunk);
    sceNetSocketClose(socket);
    g_connections.fetch_sub(1);
    return nullptr;
}

static SceNetId Listen()
{
    const SceNetId socket = sceNetSocket("bo3c_share", SCE_NET_AF_INET, SCE_NET_SOCK_STREAM, 0);

    if (socket < 0)
        return -1;

    int on = 1;
    sceNetSetsockopt(socket, SCE_NET_SOL_SOCKET, SCE_NET_SO_REUSEADDR, &on, sizeof(on));
    Tune(socket);

    SceNetSockaddrIn address{};
    address.sin_len = sizeof(address);
    address.sin_family = SCE_NET_AF_INET;
    address.sin_port = sceNetHtons(kPort);
    address.sin_addr.s_addr = SCE_NET_INADDR_ANY;

    if (sceNetBind(socket, (const SceNetSockaddr*)&address, sizeof(address)) < 0 || sceNetListen(socket, 4) < 0)
    {
        sceNetSocketClose(socket);
        return -1;
    }

    SetBlocking(socket, false);
    return socket;
}

static void* Server(void*)
{
    SceNetId listener = -1;

    for (;;)
    {
        if (!g_serve.load())
        {
            if (listener >= 0)
            {
                sceNetSocketClose(listener);
                listener = -1;
                T7Log_Write("[Share] no longer hosting a LAN lobby: stopped listening");
            }

            sceKernelUsleep(500000);
            continue;
        }

        if (listener < 0)
        {
            listener = Listen();

            if (listener < 0)
            {
                sceKernelUsleep(3000000);
                continue;
            }

            T7Log_Write("[Share] hosting a LAN lobby: listening on port %d", (int)kPort);
        }

        SceNetSockaddrIn from{};
        SceNetSocklen_t length = sizeof(from);
        const SceNetId peer = sceNetAccept(listener, (SceNetSockaddr*)&from, &length);

        if (peer < 0)
        {
            sceKernelUsleep(100000);
            continue;
        }

        if (!Private(from.sin_addr.s_addr) || g_connections.load() >= kMaxConnections)
        {
            sceNetSocketClose(peer);
            continue;
        }

        SetBlocking(peer, true);
        SetWait(peer, kSocketWaitUs);
        g_connections.fetch_add(1);

        ScePthread thread;

        if (scePthreadCreate(&thread, nullptr, Serve, (void*)(intptr_t)peer, "BO3-Customs Share Send") != 0)
        {
            g_connections.fetch_sub(1);
            sceNetSocketClose(peer);
            continue;
        }

        scePthreadDetach(thread);
    }

    return nullptr;
}

static void Roomier(int socket)
{
    static std::atomic<int> grown{-1};

    if (socket < 0 || grown.exchange(socket) == socket)
        return;

    const int options[] = { SCE_NET_SO_SNDBUF, SCE_NET_SO_RCVBUF };

    for (const int option : options)
    {
        for (int size : kSocketBuffers)
        {
            if (sceNetSetsockopt((SceNetId)socket, SCE_NET_SOL_SOCKET, option, &size, sizeof(size)) >= 0)
                break;
        }
    }
}

static void SendUdp(int socket, uint32_t address, uint16_t port, const UdpHeader& header, const void* data, size_t size)
{
    if (socket < 0 || !port)
        return;

    uint8_t packet[kUdpPacket];
    memcpy(packet, &header, sizeof(header));

    if (size)
        memcpy(packet + sizeof(header), data, size);

    SceNetSockaddrIn to{};
    to.sin_len = sizeof(to);
    to.sin_family = SCE_NET_AF_INET;
    to.sin_port = sceNetHtons(port);
    to.sin_addr.s_addr = address;
    for (int i = 0; i < kUdpSendTries; ++i)
    {
        const int sent = sceNetSendto((SceNetId)socket, packet, sizeof(header) + size, 0, (const SceNetSockaddr*)&to, sizeof(to));

        if (sent != SCE_NET_ERROR_ENOBUFS && sent != SCE_NET_ERROR_EWOULDBLOCK)
            return;

        sceKernelUsleep(kUdpSendWaitUs);
    }
}

static UdpHeader Header(UdpType type, uint32_t token, int32_t object, uint64_t offset, uint64_t total, size_t size)
{
    UdpHeader header{};
    header.magic = kUdpMagic;
    header.version = kUdpVersion;
    header.type = type;
    header.size = (uint16_t)size;
    header.token = token;
    header.object = object;
    header.offset = offset;
    header.total = total;
    return header;
}

static UdpHeader AskHeader(const Link& link, int32_t object, uint64_t offset, uint64_t count)
{
    UdpHeader header = Header(kUdpAsk, link.token, object, offset, link.version >= 2 ? count : 0, 0);
    header.version = link.version;
    return header;
}

static void Refuse(const Ask& ask, UdpRefusal why)
{
    UdpHeader header = Header(kUdpRefuse, ask.header.token, ask.header.object, ask.header.offset, why, 0);
    header.version = ask.header.version;
    SendUdp(ask.socket, ask.address, ask.port, header, nullptr, 0);
}

static void ClosePeer(UdpPeer& peer, const char* why)
{
    if (peer.token && peer.sent)
    {
        const uint8_t* const b = (const uint8_t*)&peer.address;
        const double seconds = (double)(peer.usedAt - peer.began) / 1000000.0;
        T7Log_Write("[Share] sent %llu bytes through the game's connection to %u.%u.%u.%u in %.1f s (%.2f MB/s); %s", (unsigned long long)peer.sent,
                    b[0], b[1], b[2], b[3], seconds, seconds > 0.0 ? (double)peer.sent / seconds / 1048576.0 : 0.0, why);
    }

    if (peer.fd >= 0)
        sceKernelClose(peer.fd);

    Give(peer.files, sizeof(Served) * kMaxEntries);
    Give(peer.list, kUdpListMax);
    Give(peer.cache, kUdpCache);
    memset(&peer, 0, sizeof(peer));
    peer.fd = -1;
    peer.fdId = -1;
}

static void ExpirePeers(uint64_t now, bool all)
{
    for (UdpPeer& peer : g_peers)
    {
        if (peer.token && (all || (now > peer.usedAt && now - peer.usedAt > kUdpPeerIdleUs)))
            ClosePeer(peer, all ? "the host left LAN play" : "the joiner went quiet");
    }
}

static UdpPeer* FindPeer(uint32_t token, uint32_t address)
{
    for (UdpPeer& peer : g_peers)
    {
        if (peer.token == token && peer.address == address)
            return &peer;
    }

    return nullptr;
}

static bool Append(char* list, size_t* used, const char* format, ...)
{
    va_list args;
    va_start(args, format);
    const int n = vsnprintf(list + *used, kUdpListMax - *used, format, args);
    va_end(args);

    if (n <= 0 || (size_t)n >= kUdpListMax - *used)
        return false;

    *used += (size_t)n;
    return true;
}

static UdpPeer* NewPeer(const Ask& ask, uint64_t now)
{
    const uint32_t token = ask.header.token;
    const uint32_t address = ask.address;
    UdpPeer* slot = &g_peers[0];

    for (UdpPeer& peer : g_peers)
    {
        if (!peer.token)
        {
            slot = &peer;
            break;
        }

        if (peer.usedAt < slot->usedAt)
            slot = &peer;
    }

    if (slot->token)
        ClosePeer(*slot, "a newer joiner took its place");

    const uint32_t version = g_hostedVersion.load();
    g_hostedWanted.store(true);

    for (uint64_t waited = 0; waited < kFreshListUs && g_hostedVersion.load() == version; waited += 10000)
        sceKernelUsleep(10000);

    Hosted hosted;

    {
        Spin lock(g_hostedLock);
        hosted = g_hosted;
    }

    if (!g_serve.load() || !hosted.serving)
        return nullptr;

    UdpPeer& peer = *slot;
    peer.files = (Served*)Take(sizeof(Served) * kMaxEntries);
    peer.list = (char*)Take(kUdpListMax);
    peer.cache = (char*)Take(kUdpCache);
    peer.fd = -1;
    peer.fdId = -1;

    if (!peer.files || !peer.list || !peer.cache)
    {
        ClosePeer(peer, "this console is out of memory");
        return nullptr;
    }

    if (hosted.custom)
        Walk(hosted.mapRoot, "", 0, -1, peer.files, &peer.count);

    for (int k = 0; k < hosted.modCount; ++k)
        Walk(hosted.modRoots[k], "", 0, k, peer.files, &peer.count);

    size_t used = 0;
    bool fits = Append(peer.list, &used, "BO3C 1 OK\n") &&
                Append(peer.list, &used, "map %s %d\n", hosted.map[0] ? hosted.map : "-", hosted.custom ? 1 : 0);

    for (int k = 0; fits && k < hosted.modCount; ++k)
        fits = Append(peer.list, &used, "mod %s\n", hosted.mods[k]);

    for (int i = 0; fits && i < peer.count; ++i)
        fits = Append(peer.list, &used, "file %d %d %llu %s\n", i, peer.files[i].kind, (unsigned long long)peer.files[i].size,
                      peer.files[i].rel);

    if (!fits || !Append(peer.list, &used, "end\n"))
    {
        ClosePeer(peer, "the file list did not fit");
        return nullptr;
    }

    peer.listSize = used;
    peer.token = token;
    peer.address = address;
    peer.port = ask.port;
    peer.socket = ask.socket;
    Roomier(ask.socket);
    peer.usedAt = now;
    peer.began = now;

    const uint8_t* const b = (const uint8_t*)&address;
    T7Log_Write("[Share] sent the list for %s through the game's connection to %u.%u.%u.%u: %d file(s), %d mod(s)", hosted.map, b[0], b[1], b[2],
                b[3], peer.count, hosted.modCount);
    return &peer;
}

static bool FileSlice(UdpPeer& peer, int id, uint64_t offset, const char** data, size_t* size)
{
    const Served& file = peer.files[id];

    if (peer.fdId != id)
    {
        if (peer.fd >= 0)
            sceKernelClose(peer.fd);

        peer.fd = sceKernelOpen(file.path, SCE_KERNEL_O_RDONLY, 0);
        peer.fdId = id;
        peer.cacheSize = 0;

        SceKernelStat st;

        if (peer.fd < 0 || sceKernelFstat(peer.fd, &st) != 0 || (uint64_t)st.st_size != file.size)
        {
            if (peer.fd >= 0)
                sceKernelClose(peer.fd);

            peer.fd = -1;
            peer.fdId = -1;
            return false;
        }
    }

    if (offset >= file.size)
        return false;

    const uint64_t inFile = file.size - offset;
    const uint64_t take = inFile < kUdpChunk ? inFile : kUdpChunk;

    if (offset < peer.cacheAt || offset + take > peer.cacheAt + peer.cacheSize)
    {
        const uint64_t at = offset & ~(uint64_t)0xFFFF;
        const uint64_t left = file.size - at;
        const size_t want = left < kUdpCache ? (size_t)left : kUdpCache;
        const int64_t got = TimedRead(peer.fd, peer.cache, want, (int64_t)at);

        if (got <= 0 || at + (uint64_t)got < offset + take)
        {
            peer.cacheSize = 0;
            return false;
        }

        peer.cacheAt = at;
        peer.cacheSize = (size_t)got;
    }

    *data = peer.cache + (offset - peer.cacheAt);
    *size = (size_t)take;
    return true;
}

static void AnswerUdp(const Ask& ask, uint64_t now)
{
    const UdpHeader& asked = ask.header;

    if (!asked.token)
        return;

    if (!g_serve.load())
    {
        Refuse(ask, kRefuseNotSharing);
        return;
    }

    if (asked.object == kUdpStatsObject)
    {
        uint64_t upload = 0;

        if (UdpPeer* const sender = FindPeer(asked.token, ask.address))
        {
            if (sender->statsAt && now > sender->statsAt)
            {
                const uint64_t sample = (sender->sent - sender->statsSent) * 1000000 / (now - sender->statsAt);
                sender->upload = sender->upload ? (sender->upload * 3 + sample) / 4 : sample;
            }

            sender->statsAt = now;
            sender->statsSent = sender->sent;
            upload = sender->upload;
        }

        UdpHeader header = Header(kUdpStats, asked.token, kUdpStatsObject, upload, g_readRate.load(std::memory_order_relaxed), 0);
        header.version = asked.version;
        SendUdp(ask.socket, ask.address, ask.port, header, nullptr, 0);
        return;
    }

    UdpPeer* peer = FindPeer(asked.token, ask.address);

    if (!peer)
    {
        if (asked.object != -1)
        {
            Refuse(ask, kRefuseNoFile);
            return;
        }

        peer = NewPeer(ask, now);

        if (!peer)
        {
            Refuse(ask, kRefuseNotSharing);
            return;
        }
    }

    peer->usedAt = now;
    peer->port = ask.port;
    peer->socket = ask.socket;

    const int count = asked.version >= 2 && asked.total >= 1 && asked.total <= kUdpBatch ? (int)asked.total : 1;

    for (int i = 0; i < count; ++i)
    {
        const uint64_t offset = asked.offset + (uint64_t)i * kUdpChunk;
        const char* data = nullptr;
        size_t size = 0;
        uint64_t total = 0;

        if (asked.object == -1)
        {
            if (offset >= peer->listSize)
            {
                if (i == 0)
                    Refuse(ask, kRefuseNoFile);

                return;
            }

            const size_t left = peer->listSize - (size_t)offset;
            data = peer->list + offset;
            size = left < kUdpChunk ? left : kUdpChunk;
            total = peer->listSize;
        }
        else if (asked.object >= 0 && asked.object < peer->count)
        {
            if (i > 0 && offset >= peer->files[asked.object].size)
                return;

            if (!FileSlice(*peer, asked.object, offset, &data, &size))
            {
                Refuse(ask, kRefuseChanged);
                return;
            }

            total = peer->files[asked.object].size;
            peer->sent += size;
        }
        else
        {
            Refuse(ask, kRefuseNoFile);
            return;
        }

        UdpHeader header = Header(kUdpPiece, asked.token, asked.object, offset, total, size);
        header.version = asked.version;
        SendUdp(ask.socket, ask.address, ask.port, header, data, size);
    }
}

static void Deliver(uint32_t from, const uint8_t* packet, int size)
{
    Spin lock(g_inboxLock);

    if (!g_inbox || g_inboxCount >= kUdpInbox)
        return;

    Arrival& arrival = g_inbox[(g_inboxHead + g_inboxCount) % kUdpInbox];
    arrival.from = from;
    arrival.size = size;
    memcpy(arrival.bytes, packet, (size_t)size);
    ++g_inboxCount;
}

static bool Collect(Arrival& out)
{
    Spin lock(g_inboxLock);

    if (!g_inboxCount)
        return false;

    const Arrival& arrival = g_inbox[g_inboxHead];
    out.from = arrival.from;
    out.size = arrival.size;
    memcpy(out.bytes, arrival.bytes, (size_t)arrival.size);
    g_inboxHead = (g_inboxHead + 1) % kUdpInbox;
    --g_inboxCount;
    return true;
}

static void CloseInbox()
{
    Spin lock(g_inboxLock);
    g_fetchToken.store(0);
    Give(g_inbox, sizeof(Arrival) * kUdpInbox);
    g_inbox = nullptr;
    g_inboxHead = 0;
    g_inboxCount = 0;
}

static bool OpenInbox(uint32_t token)
{
    Spin lock(g_inboxLock);

    if (!g_inbox)
        g_inbox = (Arrival*)Take(sizeof(Arrival) * kUdpInbox);

    g_inboxHead = 0;
    g_inboxCount = 0;
    g_fetchToken.store(g_inbox ? token : 0);
    return g_inbox != nullptr;
}

static void NoteRoute(int socket, uint32_t address, uint16_t port, uint64_t now)
{
    Spin lock(g_routeLock);
    Route* slot = &g_routes[0];

    for (Route& route : g_routes)
    {
        if (route.address == address)
        {
            slot = &route;
            break;
        }

        if (route.at < slot->at)
            slot = &route;
    }

    slot->address = address;
    slot->port = port;
    slot->socket = socket;
    slot->at = now;
}

static bool FindRoute(uint32_t address, int* socket, uint16_t* port)
{
    Spin lock(g_routeLock);

    for (const Route& route : g_routes)
    {
        if (route.address == address && route.port && route.at)
        {
            *socket = route.socket;
            *port = route.port;
            return true;
        }
    }

    return false;
}

static void QueueAsk(int socket, uint32_t address, uint16_t port, const UdpHeader& header)
{
    Spin lock(g_askLock);

    if (!g_asks || g_askCount >= kUdpAsks)
        return;

    Ask& ask = g_asks[(g_askHead + g_askCount) % kUdpAsks];
    ask.socket = socket;
    ask.address = address;
    ask.port = port;
    ask.header = header;
    ++g_askCount;
}

static bool TakeAsk(Ask& out)
{
    Spin lock(g_askLock);

    if (!g_askCount)
        return false;

    out = g_asks[g_askHead];
    g_askHead = (g_askHead + 1) % kUdpAsks;
    --g_askCount;
    return true;
}

static void TakeMaps(const UdpHeader& header, const uint8_t* payload, uint64_t now);
static void TakeLacks(const UdpHeader& header, const uint8_t* payload, uint64_t now);
static void TakeProgress(const UdpHeader& header, uint64_t now);
static void TakeProgressList(const UdpHeader& header, const uint8_t* payload, uint64_t now);

static int DwReceive_h(int64_t socket, uint32_t* address, uint16_t* port, void* buffer, uint32_t size)
{
    for (;;)
    {
        const int got = g_dwReceive(socket, address, port, buffer, size);

        if (got <= 0 || !address || !port || !buffer)
            return got;

        UdpHeader header;

        if (got >= (int)sizeof(header))
            memcpy(&header, buffer, sizeof(header));

        if (got < (int)sizeof(header) || header.magic != kUdpMagic || header.version < 1 || header.version > kUdpVersion ||
            header.size > got - (int)sizeof(header))
        {
            NoteRoute((int)socket, *address, *port, sceKernelGetProcessTime());
            return got;
        }

        if (header.type == kUdpMaps && header.version >= 2)
        {
            TakeMaps(header, (const uint8_t*)buffer + sizeof(header), sceKernelGetProcessTime());

            if (header.object == 0)
                QueueAsk((int)socket, *address, *port, header);
        }
        else if (header.type == kUdpLacks && header.version >= 2)
        {
            TakeLacks(header, (const uint8_t*)buffer + sizeof(header), sceKernelGetProcessTime());
        }
        else if (header.type == kUdpProgress && header.version >= 2)
        {
            TakeProgress(header, sceKernelGetProcessTime());
        }
        else if (header.type == kUdpProgressList && header.version >= 2)
        {
            TakeProgressList(header, (const uint8_t*)buffer + sizeof(header), sceKernelGetProcessTime());
        }
        else if (header.type == kUdpAsk)
            QueueAsk((int)socket, *address, *port, header);
        else if ((header.type == kUdpPiece || header.type == kUdpRefuse || header.type == kUdpStats) && header.token &&
                 header.token == g_fetchToken.load())
            Deliver(*address, (const uint8_t*)buffer, got);
    }
}

static uint32_t NameHash(const char* name)
{
    uint32_t hash = 5381;

    for (const char* c = name; *c; ++c)
        hash = hash * 33 + (uint32_t)(uint8_t)(*c >= 'A' && *c <= 'Z' ? *c + 32 : *c);

    return hash;
}

static void TakeMaps(const UdpHeader& header, const uint8_t* payload, uint64_t now)
{
    const uint64_t xuid = header.offset;
    const int total = (int)header.total;
    const int page = header.object;
    const int count = (int)header.size / (int)sizeof(uint32_t);

    if (!xuid || header.total > (uint64_t)kMapsMax || page < 0 || page >= kMapsPages || header.size % sizeof(uint32_t))
        return;

    const int pages = total ? (total + kMapsPerPage - 1) / kMapsPerPage : 1;
    const int first = page * kMapsPerPage;

    if (page >= pages || first + count > total || (page < pages - 1 && count != kMapsPerPage))
        return;

    Spin lock(g_mapsLock);
    MapsMember* member = nullptr;

    for (MapsMember& slot : g_mapsMembers)
    {
        if (slot.xuid == xuid)
            member = &slot;
    }

    for (MapsMember& slot : g_mapsMembers)
    {
        if (!member && (!slot.xuid || now - slot.seenAt > kMapsStaleUs))
        {
            if (slot.xuid && slot.known)
                g_mapsVersion.fetch_add(1);

            memset(&slot, 0, sizeof(slot));
            slot.xuid = xuid;
            member = &slot;
        }
    }

    if (!member)
        return;

    if (member->seq != header.token || member->pendingTotal != total)
    {
        member->seq = header.token;
        member->pendingTotal = total;
        member->pagesGot = 0;
    }

    memcpy(member->pending + first, payload, (size_t)count * sizeof(uint32_t));
    member->pagesGot |= 1u << page;
    member->seenAt = now;

    if (member->pagesGot != (1u << pages) - 1)
        return;

    const bool changed = !member->known || member->count != total ||
                         memcmp(member->hashes, member->pending, (size_t)total * sizeof(uint32_t)) != 0;

    if (!member->known || member->count != total)
        T7Log_Write("[Share] a player in this lobby (%016llX) has %d custom map(s)", (unsigned long long)xuid, total);
    memcpy(member->hashes, member->pending, (size_t)total * sizeof(uint32_t));
    member->count = total;
    member->known = true;
    member->pagesGot = 0;
    member->pendingTotal = -1;

    if (changed)
        g_mapsVersion.fetch_add(1);
}

static int LackingKeys(uint32_t hash, uint64_t now, uint64_t* keys, int capacity)
{
    int count = 0;
    Spin lock(g_mapsLock);

    for (const MapsMember& slot : g_mapsMembers)
    {
        if (!slot.xuid || !slot.known || now - slot.seenAt > kMapsStaleUs || count >= capacity)
            continue;

        bool have = false;

        for (int i = 0; i < slot.count && !have; ++i)
            have = slot.hashes[i] == hash;

        if (!have)
            keys[count++] = slot.xuid;
    }

    return count;
}

static void AnswerMaps(const Ask& ask, uint64_t now)
{
    if (!g_serve.load())
        return;

    char map[kT7ShareName];
    bool custom = false;

    {
        Spin lock(g_hostedLock);
        snprintf(map, sizeof(map), "%s", g_hosted.map);
        custom = g_hosted.custom;
    }

    if (!map[0])
        return;

    uint64_t keys[kMapsMembers];
    const uint32_t hash = NameHash(map);
    const int count = custom ? LackingKeys(hash, now, keys, kMapsMembers) : 0;
    UdpHeader header = Header(kUdpLacks, hash, count, 0, 0, (size_t)count * sizeof(uint64_t));
    SendUdp(ask.socket, ask.address, ask.port, header, keys, (size_t)count * sizeof(uint64_t));

    ProgressEntry entries[kMapsMembers];
    int shown = 0;

    {
        Spin lock(g_mapsLock);

        for (const MapsMember& slot : g_mapsMembers)
        {
            if (slot.xuid && slot.progressAt && now - slot.progressAt <= kProgressStaleUs && shown < kMapsMembers)
                entries[shown++] = { slot.xuid, slot.progress, slot.progressMap };
        }
    }

    SendUdp(ask.socket, ask.address, ask.port, Header(kUdpProgressList, hash, shown, 0, 0, (size_t)shown * sizeof(ProgressEntry)), entries,
            (size_t)shown * sizeof(ProgressEntry));
}

static void TakeProgress(const UdpHeader& header, uint64_t now)
{
    const uint64_t key = header.offset;
    const int percent = header.object < 0 ? -1 : header.object > 100 ? 100 : header.object;

    if (!key)
        return;

    Spin lock(g_mapsLock);
    MapsMember* member = nullptr;

    for (MapsMember& slot : g_mapsMembers)
    {
        if (slot.xuid == key)
            member = &slot;
    }

    for (MapsMember& slot : g_mapsMembers)
    {
        if (!member && (!slot.xuid || now - slot.seenAt > kMapsStaleUs))
        {
            memset(&slot, 0, sizeof(slot));
            slot.xuid = key;
            member = &slot;
        }
    }

    if (!member)
        return;

    const bool changed = member->progress != percent || !member->progressAt || now - member->progressAt > kProgressStaleUs;
    member->progress = percent;
    member->progressMap = header.token;
    member->progressAt = now;
    member->seenAt = now;

    if (changed)
        g_mapsVersion.fetch_add(1);
}

static void TakeProgressList(const UdpHeader& header, const uint8_t* payload, uint64_t now)
{
    const int count = header.object;

    if (count < 0 || count > kMapsMembers || header.size != (size_t)count * sizeof(ProgressEntry))
        return;

    Spin lock(g_lacksLock);
    const bool changed = g_progressCount != count || memcmp(g_progress, payload, (size_t)count * sizeof(ProgressEntry)) != 0;
    g_progressCount = count;
    g_progressAt = now;
    memcpy(g_progress, payload, (size_t)count * sizeof(ProgressEntry));

    if (changed)
        g_mapsVersion.fetch_add(1);
}

static int ProgressOf(const char* map, uint64_t key, bool host)
{
    const uint32_t hash = NameHash(map);
    const uint64_t now = sceKernelGetProcessTime();

    if (host)
    {
        Spin lock(g_mapsLock);

        for (const MapsMember& slot : g_mapsMembers)
        {
            if (slot.xuid == key && slot.progressAt && now - slot.progressAt <= kProgressStaleUs && slot.progressMap == hash)
                return slot.progress;
        }

        return -1;
    }

    Spin lock(g_lacksLock);

    if (now - g_progressAt > kMapsStaleUs)
        return -1;

    for (int i = 0; i < g_progressCount; ++i)
    {
        if (g_progress[i].key == key && g_progress[i].map == hash)
            return g_progress[i].percent;
    }

    return -1;
}

static void TakeLacks(const UdpHeader& header, const uint8_t* payload, uint64_t now)
{
    const int count = header.object;

    if (count < 0 || count > kMapsMembers || header.size != (size_t)count * sizeof(uint64_t))
        return;

    Spin lock(g_lacksLock);
    const bool changed = g_lacksMap != header.token || g_lacksCount != count ||
                         memcmp(g_lacksKeys, payload, (size_t)count * sizeof(uint64_t)) != 0 || now - g_lacksAt > kMapsStaleUs;
    g_lacksMap = header.token;
    g_lacksCount = count;
    g_lacksAt = now;
    memcpy(g_lacksKeys, payload, (size_t)count * sizeof(uint64_t));

    if (changed)
        g_mapsVersion.fetch_add(1);
}

static bool ToldLacking(const char* map, uint64_t key)
{
    const uint32_t hash = NameHash(map);
    const uint64_t now = sceKernelGetProcessTime();
    Spin lock(g_lacksLock);

    if (g_lacksMap != hash || now - g_lacksAt > kMapsStaleUs)
        return false;

    for (int i = 0; i < g_lacksCount; ++i)
    {
        if (g_lacksKeys[i] == key)
            return true;
    }

    return false;
}

static void ExpireMaps(uint64_t now)
{
    Spin lock(g_mapsLock);

    for (MapsMember& slot : g_mapsMembers)
    {
        if (slot.xuid && now - slot.seenAt > kMapsStaleUs)
        {
            if (slot.known)
                g_mapsVersion.fetch_add(1);

            memset(&slot, 0, sizeof(slot));
        }
    }
}

static void PublishRole(const Role& role)
{
    Spin lock(g_reportLock);
    g_reportHostCount = role.lan && role.client ? role.hostCount : 0;

    for (int i = 0; i < g_reportHostCount; ++i)
        g_reportHosts[i] = role.hosts[i];
}

static void ReportMaps(uint64_t now)
{
    static uint64_t sentAt = 0;
    static uint32_t seq = 0;
    static char     names[kMapsMax][kT7ShareName];
    static uint32_t hashes[kMapsMax];

    if (sentAt && now - sentAt < kMapsEveryUs)
        return;

    sentAt = now;
    const uint64_t xuid = g_myXuid.load();
    uint32_t hosts[kMaxHosts];
    int hostCount = 0;

    {
        Spin lock(g_reportLock);
        hostCount = g_reportHostCount;
        memcpy(hosts, g_reportHosts, sizeof(hosts));
    }

    int socket = -1;
    uint16_t port = 0;
    uint32_t host = 0;

    for (int i = 0; i < hostCount && !host; ++i)
    {
        if (FindRoute(hosts[i], &socket, &port))
            host = hosts[i];
    }

    static uint32_t toldHost = 0;
    static int toldCount = -1;

    if (!xuid || !host)
        return;

    const int count = T7Maps_MapNames(names, kMapsMax);

    if (host != toldHost || count != toldCount)
    {
        const uint8_t* const b = (const uint8_t*)&host;
        T7Log_Write("[Share] telling the host at %u.%u.%u.%u about this console's %d custom map(s)", b[0], b[1], b[2], b[3], count);
        toldHost = host;
        toldCount = count;
    }

    for (int i = 0; i < count; ++i)
        hashes[i] = NameHash(names[i]);

    const int pages = count ? (count + kMapsPerPage - 1) / kMapsPerPage : 1;
    ++seq;

    for (int page = 0; page < pages; ++page)
    {
        const int first = page * kMapsPerPage;
        const int left = count - first;
        const int take = left < kMapsPerPage ? (left > 0 ? left : 0) : kMapsPerPage;
        SendUdp(socket, host, port, Header(kUdpMaps, seq, page, xuid, (uint64_t)count, (size_t)take * sizeof(uint32_t)), hashes + first,
                (size_t)take * sizeof(uint32_t));
    }
}

static void ReportProgress(uint64_t now)
{
    static uint64_t sentAt = 0;

    if (sentAt && now - sentAt < kProgressEveryUs)
        return;

    sentAt = now;
    const int phase = g_phase.load();
    const uint64_t key = g_myXuid.load();

    if (!key || (phase != kContacting && phase != kListing && phase != kDownloading && phase != kInstalling))
        return;

    char map[kT7ShareName];
    bool needMap = false;

    {
        Spin lock(g_textLock);
        needMap = g_session.active && g_session.needMap;
        snprintf(map, sizeof(map), "%s", g_session.map);
    }

    if (!needMap || !map[0])
        return;

    uint32_t hosts[kMaxHosts];
    int hostCount = 0;

    {
        Spin lock(g_reportLock);
        hostCount = g_reportHostCount;
        memcpy(hosts, g_reportHosts, sizeof(hosts));
    }

    int socket = -1;
    uint16_t port = 0;
    uint32_t host = 0;

    for (int i = 0; i < hostCount && !host; ++i)
    {
        if (FindRoute(hosts[i], &socket, &port))
            host = hosts[i];
    }

    if (!host)
        return;

    const uint64_t total = g_total.load();
    const uint64_t done = g_done.load();
    const int percent = phase == kInstalling ? 100 : phase == kDownloading && total ? (int)(done * 100 / total) : 0;
    SendUdp(socket, host, port, Header(kUdpProgress, NameHash(map), percent > 99 && phase != kInstalling ? 99 : percent, key, done, 0), nullptr, 0);
}

static void* Asks(void*)
{
    uint64_t expiredAt = 0;

    for (;;)
    {
        Ask ask;
        bool worked = false;

        while (TakeAsk(ask))
        {
            if (ask.header.type == kUdpMaps)
                AnswerMaps(ask, sceKernelGetProcessTime());
            else
                AnswerUdp(ask, sceKernelGetProcessTime());
            worked = true;
        }

        const uint64_t now = sceKernelGetProcessTime();
        ExpirePeers(now, !g_serve.load());
        ReportMaps(now);
        ReportProgress(now);

        if (now - expiredAt >= 1000000)
        {
            expiredAt = now;
            ExpireMaps(now);
        }

        if (!worked)
            sceKernelUsleep(1000);
    }

    return nullptr;
}

static void UpdateHosted(const Role& role)
{
    if (!role.lan || !role.host)
    {
        if (g_serve.exchange(false))
        {
            Spin lock(g_banLock);

            if (g_banCount)
                T7Log_Write("[Share] stopped hosting: %d ban(s) cleared", g_banCount / 2);

            g_banCount = 0;
        }

        return;
    }

    const uint64_t now = sceKernelGetProcessTime();

    if (g_serve.load() && !g_hostedWanted.exchange(false) && now - g_hostedAt < kSnapshotUs)
        return;

    g_hostedAt = now;

    static Hosted next;
    memset(&next, 0, sizeof(next));
    next.serving = true;
    ReadMapName(next.map, sizeof(next.map));
    next.custom = next.map[0] && T7Maps_ShareMap(next.map, next.mapRoot, sizeof(next.mapRoot));
    next.modCount = T7Maps_ShareMods(next.mods, next.modRoots, kT7ShareMods);

    {
        Spin lock(g_hostedLock);
        g_hosted = next;
    }

    g_hostedVersion.fetch_add(1);
    g_serve.store(true);
}

static bool Fail(const char* format, ...)
{
    char text[160];
    va_list args;
    va_start(args, format);
    vsnprintf(text, sizeof(text), format, args);
    va_end(args);

    {
        Spin lock(g_textLock);
        snprintf(g_session.error, sizeof(g_session.error), "%s", text);
    }

    g_failedAt.store(g_stop.load() ? 0 : sceKernelGetProcessTime());
    g_phase.store(kFailed);
    T7Log_Write("[Share] %s: %s", g_session.map, text);
    return false;
}

static SceNetId Connect(uint32_t address, uint64_t limit)
{
    const SceNetId socket = sceNetSocket("bo3c_fetch", SCE_NET_AF_INET, SCE_NET_SOCK_STREAM, 0);

    if (socket < 0)
        return -1;

    Tune(socket);

    SceNetSockaddrIn host{};
    host.sin_len = sizeof(host);
    host.sin_family = SCE_NET_AF_INET;
    host.sin_port = sceNetHtons(kPort);
    host.sin_addr.s_addr = address;

    SetBlocking(socket, false);

    bool connected = false;
    bool refused = false;

    for (uint64_t waited = 0; !connected && !refused && waited < limit && !g_stop.load(); waited += kConnectStepUs)
    {
        int problem = 0;
        SceNetSocklen_t size = sizeof(problem);

        if (waited && sceNetGetsockopt(socket, SCE_NET_SOL_SOCKET, SCE_NET_SO_ERROR, &problem, &size) >= 0 && problem != 0)
        {
            refused = true;
            break;
        }

        const int result = sceNetConnect(socket, (const SceNetSockaddr*)&host, sizeof(host));

        if (result >= 0 || result == SCE_NET_ERROR_EISCONN)
            connected = true;
        else if (result == SCE_NET_ERROR_EINPROGRESS || result == SCE_NET_ERROR_EALREADY || Waited(result))
            sceKernelUsleep((unsigned int)kConnectStepUs);
        else
            refused = true;
    }

    if (!connected)
    {
        sceNetSocketClose(socket);
        return -1;
    }

    SetBlocking(socket, true);
    SetWait(socket, kSocketWaitUs);
    return socket;
}

static void MakeParents(const char* path)
{
    char folder[kPathMax];
    snprintf(folder, sizeof(folder), "%s", path);

    for (char* at = folder + 1; *at; ++at)
    {
        if (*at != '/')
            continue;

        *at = 0;
        sceKernelMkdir(folder, 0777);
        *at = '/';
    }
}

static bool Writable(const char* root, const char* kind)
{
    char path[kPathMax];
    snprintf(path, sizeof(path), "%s/%s/bo3customs-write-test.part", root, kind);
    MakeParents(path);

    const int fd = sceKernelOpen(path, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_TRUNC, 0777);

    if (fd < 0)
        return false;

    sceKernelClose(fd);
    sceKernelUnlink(path);
    return true;
}

static const char* FileName(const char* rel)
{
    const char* const slash = strrchr(rel, '/');
    return slash ? slash + 1 : rel;
}

static bool LanguageOf(const char* rel, char* code, char* shape, size_t size)
{
    static const char* const k_codes[] = { "ar", "au", "bp", "br", "ct", "cz", "du", "ea", "en", "es", "fc", "fj", "fr", "ge",
                                           "he", "it", "ja", "ko", "ms", "nl", "pl", "po", "pt", "ru", "sc", "sp", "tc" };

    const char* at = nullptr;

    if (strncmp(rel, "snd/", 4) == 0 && rel[4] && rel[5] && rel[6] == '/')
        at = rel + 4;
    else if (!strchr(rel, '/') && rel[0] && rel[1] && rel[2] == '_' && (EndsWith(rel, ".ff") || EndsWith(rel, ".xpak")))
        at = rel;

    if (!at)
        return false;

    bool known = false;

    for (const char* const candidate : k_codes)
        known = known || (candidate[0] == at[0] && candidate[1] == at[1]);

    if (!known)
        return false;

    code[0] = at[0];
    code[1] = at[1];
    code[2] = 0;

    size_t used = 0;

    for (const char* c = rel; *c && used + 1 < size; ++c)
    {
        const bool inCode = (c == at || c == at + 1);
        const bool inBank = at != rel && c > at + 2 && c[0] == '.' && c[1] == at[0] && c[2] == at[1] && c[3] == '.';
        shape[used++] = inCode ? '*' : *c;

        if (inBank && used + 3 < size)
        {
            shape[used++] = '*';
            shape[used++] = '*';
            c += 2;
        }
    }

    shape[used] = 0;
    return true;
}

static int SkipOtherLanguages(Entry* entries, int count)
{
    char ours[4];

    if (!T7Kbm_LanguagePrefix(ours, sizeof(ours)))
        return 0;

    int skipped = 0;

    for (int i = 0; i < count; ++i)
    {
        char code[4];
        char shape[kRelMax];

        if (!entries[i].wanted || !LanguageOf(entries[i].rel, code, shape, sizeof(shape)) || strcmp(code, ours) == 0)
            continue;

        int fallback = -1;
        bool covered = false;

        for (int j = 0; j < count && !covered; ++j)
        {
            char other[4];
            char otherShape[kRelMax];

            if (j == i || entries[j].kind != entries[i].kind || !LanguageOf(entries[j].rel, other, otherShape, sizeof(otherShape)) ||
                strcmp(otherShape, shape) != 0)
                continue;

            if (strcmp(other, ours) == 0)
                covered = true;
            else if (fallback < 0 && (strcmp(other, "en") == 0 || j < i))
                fallback = j;
        }

        if (covered || (fallback >= 0 && strcmp(code, "en") != 0))
        {
            entries[i].wanted = false;
            ++skipped;
        }
    }

    if (skipped)
        T7Log_Write("[Share] this console uses %s_ files; %d file(s) for other languages are left out", ours, skipped);

    return skipped;
}

static bool Marker(const Entry& entry)
{
    const char* const name = FileName(entry.rel);

    if (entry.kind < 0)
    {
        char zone[kT7ShareName + 4];
        snprintf(zone, sizeof(zone), "%s.ff", g_session.map);
        return EqualsNoCase(name, zone);
    }

    return EqualsNoCase(name, "core_mod.ff") || EqualsNoCase(name, "zm_mod.ff") || EqualsNoCase(name, "mp_mod.ff") ||
           EqualsNoCase(name, "cp_mod.ff");
}

static uint32_t NewToken()
{
    static uint32_t counter = 0;
    uint32_t token = (uint32_t)sceKernelGetProcessTime() * 2654435761u ^ (++counter * 40503u) ^ (uint32_t)(uintptr_t)&counter;
    return token ? token : 1;
}

static const char* Refusal(uint64_t code)
{
    switch (code)
    {
    case kRefuseNotSharing:
        return "the host is not sharing its files right now";
    case kRefuseNoFile:
        return "the host could not send a file";
    default:
        return "the host's files changed while downloading";
    }
}

using Sink = bool (*)(void* context, uint64_t offset, const uint8_t* data, size_t size);

static const char* UdpPull(Link& link, int32_t object, uint64_t from, uint64_t* total, Sink sink, void* context)
{
    Piece* const pieces = link.pieces;
    uint64_t chunks = *total ? (*total - from + kUdpChunk - 1) / kUdpChunk : 1;
    uint64_t base = 0;
    uint64_t next = 0;
    int inFlight = 0;
    double& window = link.window;
    bool& starting = link.starting;
    uint64_t& smoothed = link.smoothed;
    uint64_t& fastest = link.fastest;
    uint64_t& timeout = link.timeout;
    uint64_t lastScan = 0;
    uint64_t epochAt = 0;
    uint32_t epochAsked = 0;
    uint32_t epochLost = 0;
    uint64_t epochBest = 0;
    int epochPeak = 0;
    uint64_t newestAsked = 0;
    uint64_t lastHeard = sceKernelGetProcessTime();
    uint64_t pacedAt = lastHeard;
    uint64_t& reportAt = link.reportAt;
    uint64_t& reportBytes = link.reportBytes;
    double credit = window;
    const int batch = link.version >= 2 ? kUdpBatch : 1;
    uint64_t statsAt = 0;
    Arrival arrival;

    for (int i = 0; i < kUdpRing; ++i)
        pieces[i].state = 0;

    while (base < chunks)
    {
        if (g_stop.load())
            return "the download was cancelled";

        uint64_t now = sceKernelGetProcessTime();

        if (smoothed)
        {
            credit += window * (double)(now - pacedAt) / (double)(smoothed > 2000 ? smoothed : 2000);
            credit = credit > window ? window : credit;
        }
        else
        {
            credit = window;
        }

        pacedAt = now;

        while (next < chunks && inFlight < (int)window && next - base < (uint64_t)kUdpRing && credit >= 1.0)
        {
            int run = 0;

            while (run < batch && next + run < chunks && inFlight + run < (int)window &&
                   next + run - base < (uint64_t)kUdpRing && credit >= 1.0)
            {
                Piece& piece = pieces[(next + run) % kUdpRing];
                piece.state = 1;
                piece.tries = 1;
                piece.askedAt = now;
                credit -= 1.0;
                ++run;
            }

            inFlight += run;
            epochAsked += (uint32_t)run;
            SendUdp(link.socket, link.address, link.port, AskHeader(link, object, from + next * kUdpChunk, (uint64_t)run), nullptr, 0);
            next += (uint64_t)run;
        }

        epochPeak = inFlight > epochPeak ? inFlight : epochPeak;
        bool heard = false;

        while (Collect(arrival))
        {
            UdpHeader header;
            memcpy(&header, arrival.bytes, sizeof(header));

            if (header.token != link.token)
                continue;

            if (header.type == kUdpStats)
            {
                if (header.object == kUdpStatsObject)
                {
                    g_hostRead.store(header.total);
                    g_hostUpload.store(header.offset);
                }

                continue;
            }

            if (header.object != object)
                continue;

            if (header.type == kUdpRefuse)
                return Refusal(header.total);

            if (header.offset < from || (header.offset - from) % kUdpChunk != 0)
                continue;

            if (!*total)
            {
                if (header.total <= from || header.total > kUdpListMax)
                    return "the host's file list is too long";

                *total = header.total;
                chunks = (*total - from + kUdpChunk - 1) / kUdpChunk;
            }
            else if (header.total != *total)
            {
                return "the host's files changed while downloading";
            }

            const uint64_t k = (header.offset - from) / kUdpChunk;

            if (k < base || k >= next)
                continue;

            Piece& piece = pieces[k % kUdpRing];
            const uint64_t left = *total - header.offset;
            const size_t expected = left < kUdpChunk ? (size_t)left : kUdpChunk;

            if (piece.state != 1 || header.size != expected)
                continue;

            if (!sink(context, header.offset, arrival.bytes + sizeof(UdpHeader), expected))
                return "this console ran out of room";

            piece.state = 2;
            reportBytes += expected;
            g_received.fetch_add(expected);
            --inFlight;
            heard = true;
            now = sceKernelGetProcessTime();

            if (piece.askedAt > newestAsked)
                newestAsked = piece.askedAt;

            if (piece.tries == 1 && object >= 0)
            {
                const uint64_t sample = now - piece.askedAt;
                const uint64_t second = now / 1000000;
                const int slot = (int)(second % kUdpLowSlots);

                if (link.lowAt[slot] != second || !link.lows[slot] || sample < link.lows[slot])
                {
                    link.lows[slot] = link.lowAt[slot] != second || !link.lows[slot] ? sample : link.lows[slot] < sample ? link.lows[slot] : sample;
                    link.lowAt[slot] = second;
                }

                smoothed = smoothed ? (smoothed * 7 + sample) / 8 : sample;
                epochBest = !epochBest || sample < epochBest ? sample : epochBest;
                timeout = smoothed * 2 + 30000;
                timeout = timeout < kUdpRtoMinUs ? kUdpRtoMinUs : timeout > kUdpRtoMaxUs ? kUdpRtoMaxUs : timeout;
            }
        }

        while (base < next && pieces[base % kUdpRing].state == 2)
        {
            pieces[base % kUdpRing].state = 0;
            ++base;
        }

        now = sceKernelGetProcessTime();

        if (heard)
        {
            lastHeard = now;
        }
        else if (now - lastHeard > kUdpQuietUs)
        {
            return "the host stopped answering";
        }

        if (now - lastScan >= kUdpScanUs)
        {
            lastScan = now;
            bool lost = false;
            const bool silent = now - lastHeard > timeout;

            for (uint64_t k = base; k < next; ++k)
            {
                Piece& piece = pieces[k % kUdpRing];

                if (piece.state != 1)
                    continue;

                const uint64_t waited = now - piece.askedAt;
                const bool overtaken = piece.askedAt < newestAsked && smoothed && waited > smoothed + smoothed / 4;

                if (waited < timeout && !overtaken)
                    continue;

                if (credit < 1.0)
                    break;

                credit -= 1.0;
                piece.askedAt = now;
                piece.tries = piece.tries < 255 ? piece.tries + 1 : 255;
                ++link.resent;
                ++epochLost;
                lost = true;
                SendUdp(link.socket, link.address, link.port, AskHeader(link, object, from + k * kUdpChunk, 1), nullptr, 0);
            }

            if (smoothed && epochBest && now - epochAt >= smoothed)
            {
                const uint64_t second = now / 1000000;
                fastest = 0;

                for (int i = 0; i < kUdpLowSlots; ++i)
                {
                    if (link.lows[i] && second - link.lowAt[i] <= kUdpLowSpanS && (!fastest || link.lows[i] < fastest))
                        fastest = link.lows[i];
                }

                const bool flooded = epochAsked >= 16 && epochLost * 100 > epochAsked * 15;
                const bool lossy = epochAsked >= 16 && epochLost * 100 > epochAsked * 3;
                const bool swelling = smoothed > fastest * 2 + 20000;
                const bool queued = epochBest > fastest + (fastest > 50000 ? fastest : 50000) || smoothed > fastest * 2 + 100000;
                const bool used = epochPeak * 4 >= (int)window * 3;

                const bool rising = starting && (epochBest > fastest + fastest / 4 + 5000 || smoothed > fastest + fastest / 2 + 10000);

                if (flooded || queued || (lossy && (starting || swelling)))
                {
                    starting = false;
                    window *= flooded ? 0.8 : 0.9;
                }
                else if (rising)
                {
                    starting = false;
                }
                else if (used)
                {
                    window = starting ? window * 2.0 : window + (window / 8.0 > 2.0 ? window / 8.0 : 2.0);
                }

                window = window < kUdpWindowMin ? kUdpWindowMin : window > kUdpWindowMax ? kUdpWindowMax : window;
                epochAt = now;
                epochAsked = 0;
                epochLost = 0;
                epochBest = 0;
                epochPeak = inFlight;
            }

            if (lost && silent)
                timeout = timeout * 2 > kUdpRtoMaxUs ? kUdpRtoMaxUs : timeout * 2;
        }

        if (link.version >= 2 && object >= 0 && now - statsAt >= kUdpStatsEveryUs)
        {
            statsAt = now;
            SendUdp(link.socket, link.address, link.port, AskHeader(link, kUdpStatsObject, 0, 1), nullptr, 0);
        }

        if (now - reportAt >= kUdpReportUs)
        {
            const uint64_t writeUs = g_writeUs.load();
            T7Log_Write("[Share] downloading at %llu KB/s: window %d, %d in flight, round trip %llu ms (best %llu ms), %llu asked again; "
                        "host reads %llu KB/s, host uploads %llu KB/s, this console writes %llu KB/s",
                        (unsigned long long)(reportBytes * 1000000 / (now - reportAt) / 1024), (int)window, inFlight,
                        (unsigned long long)(smoothed / 1000), (unsigned long long)(fastest / 1000), (unsigned long long)link.resent,
                        (unsigned long long)(g_hostRead.load() / 1024), (unsigned long long)(g_hostUpload.load() / 1024),
                        (unsigned long long)(writeUs ? g_writeBytes.load() * 1000000 / writeUs / 1024 : 0));
            reportAt = now;
            reportBytes = 0;
        }

        if (!heard)
            sceKernelUsleep(1000);
    }

    return nullptr;
}

struct ListSink
{
    char*  list;
    size_t used;
};

static bool ToList(void* context, uint64_t offset, const uint8_t* data, size_t size)
{
    ListSink& sink = *(ListSink*)context;

    if (offset > kUdpListMax || size > kUdpListMax - offset)
        return false;

    memcpy(sink.list + offset, data, size);

    if (offset + size > sink.used)
        sink.used = (size_t)(offset + size);

    return true;
}

static bool WriteAll(int fd, const void* data, size_t size)
{
    const uint64_t began = sceKernelGetProcessTime();

    for (size_t written = 0; written < size;)
    {
        const int64_t n = sceKernelWrite(fd, (const char*)data + written, size - written);

        if (n <= 0)
            return false;

        written += (size_t)n;
    }

    g_writeUs.fetch_add(sceKernelGetProcessTime() - began);
    g_writeBytes.fetch_add(size);
    return true;
}

struct FileSink
{
    int         fd;
    char*       buffer;
    size_t      used;
    uint64_t    from;
    uint64_t    end;
    Slot*       slots;
    int         spill;
    const char* spillPath;
};

static bool Flush(FileSink& sink)
{
    if (sink.used && !WriteAll(sink.fd, sink.buffer, sink.used))
        return false;

    sink.used = 0;
    return true;
}

static bool Append(FileSink& sink, const uint8_t* data, size_t size)
{
    if (sink.used + size > kChunk && !Flush(sink))
        return false;

    memcpy(sink.buffer + sink.used, data, size);
    sink.used += size;
    sink.end += size;
    g_done.fetch_add(size);
    return true;
}

static uint64_t SlotOf(const FileSink& sink, uint64_t offset)
{
    return (offset - sink.from) / kUdpChunk % kUdpRing;
}

static bool Park(FileSink& sink, uint64_t offset, const uint8_t* data, size_t size)
{
    if (sink.spill < 0)
        sink.spill = sceKernelOpen(sink.spillPath, SCE_KERNEL_O_RDWR | SCE_KERNEL_O_CREAT | SCE_KERNEL_O_TRUNC, 0777);

    const uint64_t slot = SlotOf(sink, offset);

    if (sink.spill < 0 || sceKernelPwrite(sink.spill, data, size, (off_t)(slot * kUdpChunk)) != (int64_t)size)
        return false;

    sink.slots[slot].at = offset;
    sink.slots[slot].size = (uint32_t)size;
    return true;
}

static bool ToFile(void* context, uint64_t offset, const uint8_t* data, size_t size)
{
    FileSink& sink = *(FileSink*)context;

    if (offset != sink.end)
        return offset < sink.end || Park(sink, offset, data, size);

    if (!Append(sink, data, size))
        return false;

    uint8_t parked[kUdpChunk];

    for (;;)
    {
        const uint64_t slot = SlotOf(sink, sink.end);
        Slot& held = sink.slots[slot];

        if (!held.size || held.at != sink.end)
            return true;

        const size_t got = held.size;
        held.size = 0;

        if (sceKernelPread(sink.spill, parked, got, (off_t)(slot * kUdpChunk)) != (int64_t)got || !Append(sink, parked, got))
            return false;
    }
}

static bool NextLine(Link& link, char* out, size_t size)
{
    if (link.stream)
        return ReadLine(*link.stream, out, size);

    size_t used = 0;

    while (link.listAt < link.listSize)
    {
        const char c = link.list[link.listAt++];

        if (c == '\n')
        {
            out[used] = 0;
            return true;
        }

        if (c == '\r')
            continue;

        if (used + 1 >= size)
            return false;

        out[used++] = c;
    }

    return false;
}

static bool Fetch(Link& link, int id, const Entry& entry, const char* target, char* buffer)
{
    SceKernelStat st;

    if (sceKernelStat(target, &st) == 0 && (uint64_t)st.st_size == entry.size)
    {
        g_done.fetch_add(entry.size);
        return true;
    }

    char part[kPathMax];
    char spill[kPathMax];

    if ((size_t)snprintf(part, sizeof(part), "%s.part", target) >= sizeof(part) ||
        (size_t)snprintf(spill, sizeof(spill), "%s.spill.part", target) >= sizeof(spill))
        return Fail("the path of %s is too long", entry.rel);

    sceKernelUnlink(spill);

    uint64_t offset = 0;

    if (sceKernelStat(part, &st) == 0)
    {
        if (st.st_size > 0 && (uint64_t)st.st_size <= entry.size)
            offset = (uint64_t)st.st_size;
        else
            sceKernelUnlink(part);
    }

    MakeParents(target);

    const int fd = sceKernelOpen(part, SCE_KERNEL_O_WRONLY | SCE_KERNEL_O_CREAT |
                                           (offset ? SCE_KERNEL_O_APPEND : SCE_KERNEL_O_TRUNC), 0777);

    if (fd < 0)
        return Fail("this console could not write %s", entry.rel);

    g_done.fetch_add(offset);

    const char* problem = nullptr;

    if (offset < entry.size && !link.stream)
    {
        uint64_t total = entry.size;
        FileSink sink{ fd, buffer, 0, offset, offset, link.slots, -1, spill };
        memset(link.slots, 0, sizeof(Slot) * kUdpRing);
        problem = UdpPull(link, id, offset, &total, ToFile, &sink);

        if (!Flush(sink) && !problem)
            problem = "this console ran out of room";

        if (sink.spill >= 0)
        {
            sceKernelClose(sink.spill);
            sceKernelUnlink(spill);
        }
    }
    else if (offset < entry.size)
    {
        Stream& stream = *link.stream;
        char line[128];
        char* after = line;
        uint64_t left = 0;

        if (!SendText(stream, "GET %d %llu\n", id, (unsigned long long)offset) || !ReadLine(stream, line, sizeof(line)))
            problem = "the host stopped answering";
        else if (strncmp(line, "DATA ", 5) != 0 || (left = strtoull(line + 5, &after, 10)) != entry.size - offset)
            problem = "the host could not send a file";
        else
        {
            const uint64_t read = strtoull(after, &after, 10);
            const uint64_t upload = strtoull(after, nullptr, 10);

            if (read)
                g_hostRead.store(read);

            if (upload)
                g_hostUpload.store(upload);
        }

        while (!problem && left > 0)
        {
            const int want = left < kChunk ? (int)left : (int)kChunk;
            const int got = ReadBody(stream, buffer, want);

            if (got <= 0)
            {
                problem = "the host stopped sending";
                break;
            }

            g_received.fetch_add((uint64_t)got);

            if (!WriteAll(fd, buffer, (size_t)got))
            {
                problem = "this console ran out of room";
                break;
            }

            left -= (uint64_t)got;
            g_done.fetch_add((uint64_t)got);
        }
    }

    sceKernelFsync(fd);
    sceKernelClose(fd);

    if (problem)
        return Fail("%s", g_stop.load() ? "the download was cancelled" : problem);

    sceKernelUnlink(target);

    if (sceKernelRename(part, target) < 0)
        return Fail("this console could not save %s", entry.rel);

    return true;
}

static bool ReadList(Link& link, Entry* entries, int* count, char* hostMap, bool* custom,
                     char mods[][kT7ShareName], int* modCount)
{
    char line[512];

    if (link.stream)
    {
        if (!SendText(*link.stream, "BO3C 1 LIST\n"))
            return Fail("the host did not answer");
    }
    else
    {
        ListSink sink{ link.list, 0 };
        uint64_t total = 0;

        if (const char* const problem = UdpPull(link, -1, 0, &total, ToList, &sink))
            return Fail("%s", problem);

        link.listSize = sink.used;
        link.listAt = 0;
    }

    if (!NextLine(link, line, sizeof(line)))
        return Fail("the host did not answer");

    if (strcmp(line, "BO3C 1 OK") != 0)
        return Fail("the host is not sharing its files right now");

    while (NextLine(link, line, sizeof(line)))
    {
        if (strcmp(line, "end") == 0)
            return true;

        if (strncmp(line, "map ", 4) == 0)
        {
            char* const space = strchr(line + 4, ' ');

            if (space)
            {
                *space = 0;
                *custom = space[1] == '1';
            }

            snprintf(hostMap, kT7ShareName, "%s", line + 4);
        }
        else if (strncmp(line, "mod ", 4) == 0)
        {
            if (*modCount >= kT7ShareMods || !SafeFolder(line + 4))
                return Fail("the host sent a mod name this console cannot use");

            snprintf(mods[(*modCount)++], kT7ShareName, "%s", line + 4);
        }
        else if (strncmp(line, "file ", 5) == 0)
        {
            char* at = line + 5;
            const long id = strtol(at, &at, 10);
            const long kind = strtol(at, &at, 10);
            const uint64_t size = strtoull(at, &at, 10);

            while (*at == ' ')
                ++at;

            if (id != *count || *count >= kMaxEntries || kind < -1 || kind >= *modCount || !SafeRel(at))
                return Fail("the host sent a file list this console cannot use");

            Entry& entry = entries[(*count)++];
            entry.size = size;
            entry.kind = (int32_t)kind;
            entry.wanted = false;
            snprintf(entry.rel, sizeof(entry.rel), "%s", at);
        }
    }

    return Fail("%s", g_stop.load() ? "the download was cancelled" : "the host's file list was cut off");
}

static bool Exchange(Link& link, Entry* entries, char* buffer, bool lastTry)
{
    Session& session = g_session;
    char hostMap[kT7ShareName] = "";
    char mods[kT7ShareMods][kT7ShareName];
    bool custom = false;
    int count = 0;
    int modCount = 0;

    g_phase.store(kListing);

    if (!ReadList(link, entries, &count, hostMap, &custom, mods, &modCount))
        return false;

    if (session.needMap && !EqualsNoCase(hostMap, session.map))
    {
        if (lastTry)
        {
            Fail("the host is on another map");
            g_failedAt.store(0);
        }

        return !lastTry;
    }

    if (session.needMap && !custom)
    {
        T7Log_Write("[Share] %s is not a custom map on the host, so there is nothing to download for it", hostMap);

        Spin lock(g_textLock);
        session.needMap = false;
    }

    bool missing[kT7ShareMods];
    int newMods = 0;

    for (int k = 0; k < modCount; ++k)
    {
        missing[k] = !T7Maps_HasMod(mods[k]);
        newMods += missing[k] ? 1 : 0;
    }

    uint64_t total = 0;
    int files = 0;

    for (int i = 0; i < count; ++i)
    {
        Entry& entry = entries[i];
        entry.wanted = entry.kind < 0 ? session.needMap : missing[entry.kind];
    }

    SkipOtherLanguages(entries, count);

    for (int i = 0; i < count; ++i)
    {
        const Entry& entry = entries[i];

        if (entry.wanted)
        {
            total += entry.size;
            ++files;
        }
    }

    {
        Spin lock(g_textLock);
        session.modCount = modCount;
        session.newMods = newMods;
        session.listed = true;
        session.fetched = files > 0;

        for (int k = 0; k < modCount; ++k)
            snprintf(session.mods[k], kT7ShareName, "%s", mods[k]);
    }

    T7Log_Write("[Share] the host runs %s with %d mod(s); %d file(s) to download (%llu bytes)", hostMap, modCount, files,
                (unsigned long long)total);

    if (files > 0)
    {
        char mapsRoot[kT7SharePath];
        char modsRoot[kT7SharePath];
        T7Maps_ShareRoot(false, mapsRoot, sizeof(mapsRoot));
        T7Maps_ShareRoot(true, modsRoot, sizeof(modsRoot));

        if (session.needMap && !Writable(mapsRoot, "usermaps"))
            snprintf(mapsRoot, sizeof(mapsRoot), "%s", "/data/BO3-Customs");

        if (newMods > 0 && !Writable(modsRoot, "mods"))
            snprintf(modsRoot, sizeof(modsRoot), "%s", "/data/BO3-Customs");

        g_total.store(total);
        g_done.store(0);
        g_received.store(0);
        g_writeBytes.store(0);
        g_writeUs.store(0);
        g_hostRead.store(0);
        g_hostUpload.store(0);
        g_fileCount.store(files);
        g_fileAt.store(0);
        g_phase.store(kDownloading);

        const uint64_t began = sceKernelGetProcessTime();

        T7Log_Write("[Share] maps go to %s, mods go to %s", mapsRoot, modsRoot);
        Notify("BO3 Customs\nDownloading %s from the host", session.needMap ? session.map : "the host's mods");

        for (int pass = 0; pass < 2; ++pass)
        {
            for (int i = 0; i < count; ++i)
            {
                const Entry& entry = entries[i];

                if (!entry.wanted || Marker(entry) != (pass == 1))
                    continue;

                if (g_stop.load())
                {
                    Fail("the download was cancelled");
                    return false;
                }

                const int number = g_fileAt.fetch_add(1) + 1;

                {
                    Spin lock(g_textLock);
                    snprintf(session.current, sizeof(session.current), "%s", FileName(entry.rel));
                }

                T7Log_Write("[Share] file %d of %d: %s (%llu bytes)", number, files, entry.rel,
                            (unsigned long long)entry.size);

                char target[kPathMax];

                if (entry.kind < 0)
                    snprintf(target, sizeof(target), "%s/usermaps/%s/%s", mapsRoot, session.map, entry.rel);
                else
                    snprintf(target, sizeof(target), "%s/mods/%s/%s", modsRoot, mods[entry.kind], entry.rel);

                if (!Fetch(link, i, entry, target, buffer))
                    return false;
            }
        }

        const double seconds = (double)(sceKernelGetProcessTime() - began) / 1000000.0;

        if (link.stream)
            T7Log_Write("[Share] %d file(s) are here: %llu bytes in %.1f s (%.2f MB/s)", files, (unsigned long long)total,
                        seconds, seconds > 0.0 ? (double)total / seconds / 1048576.0 : 0.0);
        else
            T7Log_Write("[Share] %d file(s) are here through the game's connection: %llu bytes in %.1f s (%.2f MB/s), %llu piece(s) asked again", files,
                        (unsigned long long)total, seconds, seconds > 0.0 ? (double)total / seconds / 1048576.0 : 0.0,
                        (unsigned long long)link.resent);
    }

    if (link.stream)
        SendText(*link.stream, "BYE\n");
    g_phase.store(kInstalling);
    return false;
}

static SceNetId Reach(Session& session)
{
    const uint64_t limit = session.hostCount > 1 ? kConnectManyUs : kConnectWaitUs;

    for (int i = -1; i < session.hostCount && !g_stop.load(); ++i)
    {
        const uint32_t address = i < 0 ? session.reached : session.hosts[i];

        if (!address || (i >= 0 && address == session.reached))
            continue;

        const SceNetId socket = Connect(address, limit);
        const uint8_t* const b = (const uint8_t*)&address;

        if (socket >= 0)
        {
            if (session.reached != address)
                T7Log_Write("[Share] the host's file share answered at %u.%u.%u.%u", b[0], b[1], b[2], b[3]);

            session.reached = address;
            return socket;
        }

        T7Log_Write("[Share] no answer from %u.%u.%u.%u port %d", b[0], b[1], b[2], b[3], (int)kPort);
    }

    return -1;
}

static int ReachUdp(Session& session, Link& link)
{
    if (!g_udpReady)
    {
        T7Log_Write("[Share] the game's connection cannot carry downloads in this build, so there is no second way to ask");
        return 0;
    }

    uint32_t candidates[kMaxHosts + 1];
    uint16_t blobPorts[kMaxHosts + 1];
    int count = 0;

    for (int i = -1; i < session.hostCount; ++i)
    {
        const uint32_t address = i < 0 ? session.reached : session.hosts[i];
        bool known = !address;

        for (int k = 0; k < count && !known; ++k)
            known = candidates[k] == address;

        if (known)
            continue;

        uint16_t port = 0;

        for (int k = 0; k < session.hostCount; ++k)
        {
            if (session.hosts[k] == address)
                port = session.ports[k];
        }

        blobPorts[count] = port;
        candidates[count++] = address;
    }

    if (!count)
        return 0;

    link.token = NewToken();
    link.window = kUdpWindowStart;
    link.starting = true;
    link.smoothed = 0;
    link.fastest = 0;
    link.timeout = 600000;
    memset(link.lows, 0, sizeof(link.lows));
    memset(link.lowAt, 0, sizeof(link.lowAt));
    link.reportAt = sceKernelGetProcessTime();
    link.reportBytes = 0;
    link.version = kUdpVersion;

    if (!OpenInbox(link.token))
    {
        Fail("this console is low on memory");
        return -1;
    }

    T7Log_Write("[Share] asking through the game's own connection to the host instead");

    int sockets[kMaxHosts + 1];
    uint16_t ports[kMaxHosts + 1];
    const uint64_t began = sceKernelGetProcessTime();
    uint64_t probed = 0;
    bool routed = false;
    uint64_t olderAt = 0;
    uint32_t olderFrom = 0;
    Arrival arrival;

    auto settle = [&](uint32_t from, uint8_t version) -> int
    {
        for (int i = 0; i < count; ++i)
        {
            if (candidates[i] == from && sockets[i] >= 0)
            {
                link.socket = sockets[i];
                link.port = ports[i];
            }
        }

        if (link.socket < 0 && !FindRoute(from, &link.socket, &link.port))
            return 0;

        const uint8_t* const b = (const uint8_t*)&from;
        link.address = from;
        link.version = version;
        session.reached = from;
        Roomier(link.socket);
        T7Log_Write(version >= kUdpVersion ? "[Share] the host's file share answered through the game's connection at %u.%u.%u.%u"
                                           : "[Share] the host's file share answered through the game's connection at %u.%u.%u.%u (older SPRX, one piece per ask)",
                    b[0], b[1], b[2], b[3]);
        return 1;
    };

    while (!g_stop.load())
    {
        const uint64_t now = sceKernelGetProcessTime();

        if (now - began > kUdpReachUs)
            break;

        if (!probed || now - probed >= kUdpProbeEveryUs)
        {
            probed = now;

            for (int i = 0; i < count; ++i)
            {
                sockets[i] = -1;
                ports[i] = 0;

                if (!FindRoute(candidates[i], &sockets[i], &ports[i]))
                    continue;

                if (!routed)
                {
                    const uint8_t* const b = (const uint8_t*)&candidates[i];
                    T7Log_Write("[Share] the host's game traffic comes from %u.%u.%u.%u port %u (the lobby lists port %u)", b[0], b[1],
                                b[2], b[3], (unsigned)ports[i], (unsigned)blobPorts[i]);
                    routed = true;
                }

                SendUdp(sockets[i], candidates[i], ports[i], Header(kUdpAsk, link.token, -1, 0, 0, 0), nullptr, 0);

                UdpHeader older = Header(kUdpAsk, link.token, -1, 0, 0, 0);
                older.version = 1;
                SendUdp(sockets[i], candidates[i], ports[i], older, nullptr, 0);
            }
        }

        while (Collect(arrival))
        {
            UdpHeader header;
            memcpy(&header, arrival.bytes, sizeof(header));

            if (header.token != link.token)
                continue;

            const uint8_t* const b = (const uint8_t*)&arrival.from;

            if (header.type == kUdpRefuse)
            {
                T7Log_Write("[Share] %u.%u.%u.%u answered: %s", b[0], b[1], b[2], b[3], Refusal(header.total));
                Fail("%s", Refusal(header.total));
                return -1;
            }

            if (header.version < kUdpVersion)
            {
                if (!olderAt)
                {
                    olderAt = now;
                    olderFrom = arrival.from;
                }

                continue;
            }

            if (settle(arrival.from, header.version))
                return 1;
        }

        if (olderAt && now - olderAt >= kUdpSettleUs && settle(olderFrom, 1))
            return 1;

        sceKernelUsleep(2000);
    }

    if (olderAt && !g_stop.load() && settle(olderFrom, 1))
        return 1;

    T7Log_Write(routed ? "[Share] the host did not answer through the game's connection (it may not run this SPRX)"
                       : "[Share] no game traffic from the host to answer through");
    return 0;
}

static int Open(Session& session, Link& link, Stream* stream)
{
    memset(stream, 0, sizeof(*stream));
    stream->stop = &g_stop;
    stream->socket = Reach(session);

    if (stream->socket >= 0)
    {
        link.stream = stream;
        return 1;
    }

    link.stream = nullptr;
    link.socket = -1;
    link.port = 0;

    if (!link.pieces)
    {
        T7Log_Write("[Share] %llu KB of memory free before the download buffers",
                    (unsigned long long)(FreeMemory() / 1024));
        link.pieces = (Piece*)Take(sizeof(Piece) * kUdpRing);
    }

    if (!link.slots)
        link.slots = (Slot*)Take(sizeof(Slot) * kUdpRing);

    if (!link.list)
        link.list = (char*)Take(kUdpListMax);

    if (!link.pieces || !link.slots || !link.list)
    {
        Fail("this console is low on memory");
        return -1;
    }

    return ReachUdp(session, link);
}

static void Close(Link& link)
{
    if (link.stream)
        sceNetSocketClose(link.stream->socket);

    link.stream = nullptr;
    CloseInbox();
}

static void* Fetcher(void*)
{
    Session& session = g_session;
    Stream* const stream = (Stream*)Take(sizeof(Stream));
    Entry* const entries = (Entry*)Take(sizeof(Entry) * kMaxEntries);
    char* const buffer = (char*)Take(kChunk);

    const bool deaf = !session.needMap && g_deafHost.load() == session.hostIp &&
                      sceKernelGetProcessTime() - g_deafSince.load() < kDeafHostUs;

    if (deaf)
    {
        Fail("the host did not answer a moment ago");
    }
    else if (stream && entries && buffer)
    {
        Link link{};

        for (int attempt = 1; attempt <= kListTries; ++attempt)
        {
            const int opened = Open(session, link, stream);

            if (opened <= 0)
            {
                g_deafHost.store(session.hostIp);
                g_deafSince.store(sceKernelGetProcessTime());

                if (opened == 0)
                    Fail("this console could not reach the host's file share");

                break;
            }

            g_deafHost.store(0);

            const bool again = Exchange(link, entries, buffer, attempt == kListTries);
            Close(link);

            if (!again)
                break;

            sceKernelUsleep((unsigned int)kListRetryUs);

            if (g_stop.load())
            {
                Fail("the download was cancelled");
                break;
            }
        }

        Close(link);
        Give(link.pieces, sizeof(Piece) * kUdpRing);
        Give(link.slots, sizeof(Slot) * kUdpRing);
        Give(link.list, kUdpListMax);
    }
    else
    {
        Fail("this console is out of memory");
    }

    Give(stream, sizeof(Stream));
    Give(entries, sizeof(Entry) * kMaxEntries);
    Give(buffer, kChunk);

    if (g_phase.load() == kFailed && session.needMap && !g_stop.load() && g_failedAt.load() != 0)
    {
        snprintf(g_noticeMap, sizeof(g_noticeMap), "%s", session.map);
        snprintf(g_noticeText, sizeof(g_noticeText), "%s", session.error);
        g_noticeHost = session.hostIp;
        g_noticeAt.store(sceKernelGetProcessTime());
    }

    g_busy.store(false);
    return nullptr;
}

static void Start(const Role& role, const char* map)
{
    Session& session = g_session;

    {
        Spin lock(g_textLock);
        memset(&session, 0, sizeof(session));
        session.active = true;
        session.hostIp = role.hostIp;
        session.hostCount = role.hostCount;
        memcpy(session.hosts, role.hosts, sizeof(session.hosts));
        memcpy(session.ports, role.ports, sizeof(session.ports));
        snprintf(session.map, sizeof(session.map), "%s", map);
        session.needMap = !T7Maps_HasMap(map) && !T7Maps_EngineHasMap(map);
    }

    g_total.store(0);
    g_done.store(0);
    g_received.store(0);
    g_writeBytes.store(0);
    g_writeUs.store(0);
    g_hostRead.store(0);
    g_hostUpload.store(0);
    g_fileAt.store(0);
    g_fileCount.store(0);
    g_stop.store(false);
    g_phase.store(kContacting);
    g_busy.store(true);

    const uint8_t* const b = (const uint8_t*)&role.hostIp;
    T7Log_Write("[Share] joined %u.%u.%u.%u on %s (%s)", b[0], b[1], b[2], b[3], map,
                session.needMap ? "this console does not have that map" : "the map is here");

    ScePthread thread;

    if (scePthreadCreate(&thread, nullptr, Fetcher, nullptr, "BO3-Customs Share Fetch") != 0)
    {
        g_busy.store(false);
        Fail("the download could not start");
        return;
    }

    scePthreadDetach(thread);
}

static void Finish()
{
    Session& session = g_session;

    if (session.fetched)
    {
        if (!T7Maps_Refresh())
            return;

        g_mapsChanged.store(true);
    }

    if (session.needMap && !T7Maps_HasMap(session.map))
    {
        Fail("the files arrived but the map is not complete");
        Notify("BO3 Customs\nCould not install %s", session.map);
        return;
    }

    if (session.listed)
    {
        T7Maps_SetSessionMods(session.mods, session.modCount);
        g_overriding = true;
    }

    g_phase.store(kReady);

    if (session.fetched)
        Notify("BO3 Customs\nDownloaded %s from the host", session.needMap ? session.map : "the host's mods");
}

static void KeepJoinAlive(uint64_t now)
{
    if (!g_keptAt)
    {
        g_keptAt = now;
        g_keptRest = 0;
    }

    const uint64_t passed = now - g_keptAt + g_keptRest;
    const int32_t whole = (int32_t)(passed / 1000);

    g_keptRest = passed % 1000;
    g_keptAt = now;

    for (size_t lobby = 0; whole > 0 && lobby < 2; ++lobby)
    {
        const uintptr_t session = g_base + kClientSessions + kClientSessionSize * lobby;

        if (*(const int32_t*)(session + kSessionActive) > 0 && *(const int32_t*)(session + kSessionHostType) == kAddressIp)
            *(int32_t*)(session + kSessionHostHeard) += whole;
    }

    uintptr_t task = *(const uintptr_t*)(g_base + kTaskList);

    for (int walked = 0; task > 0x10000 && walked < kTaskWalk; ++walked)
    {
        if (*(const uintptr_t*)(task + kTaskDefinition) == g_base + kLaunchTask &&
            *(const int32_t*)(task + kTaskTimeout) != -1)
        {
            *(int32_t*)(task + kTaskTimeout) = -1;
            T7Log_Write("[Share] the launch step now waits as long as the download takes");
        }

        task = *(const uintptr_t*)task;
    }
}

static bool StillOn(const char* map)
{
    char now[kT7ShareName] = "";
    return ReadMapName(now, sizeof(now)) && EqualsNoCase(now, map);
}

static void ShowNotice(const Role& role, uint64_t now)
{
    const uint64_t at = g_noticeAt.load();

    if (!at || now - at < kNoticeDelayUs)
        return;

    g_noticeAt.store(0);

    const Session& session = g_session;

    if (!role.client || role.hostIp != g_noticeHost || !session.active || g_phase.load() != kFailed ||
        !EqualsNoCase(session.map, g_noticeMap) || !StillOn(g_noticeMap))
    {
        T7Log_Write("[Share] %s: the lobby moved on, so the failure is not shown", g_noticeMap);
        return;
    }

    if (g_noticedHost == g_noticeHost && EqualsNoCase(g_noticedMap, g_noticeMap) && now - g_noticedAt < kNoticeRepeatUs)
        return;

    g_noticedHost = g_noticeHost;
    g_noticedAt = now;
    snprintf(g_noticedMap, sizeof(g_noticedMap), "%s", g_noticeMap);
    Notify("BO3 Customs\nCould not download %s: %s", g_noticeMap, g_noticeText);
}

static bool Evaluate(bool frame)
{
    bool taken = false;

    if (!g_evaluating.compare_exchange_strong(taken, true))
        return g_lastHold.load();

    const Role role = ReadRole();

    if (frame)
    {
        UpdateHosted(role);
        PublishRole(role);
    }

    char map[kT7ShareName] = "";
    const bool client = role.client && ReadMapName(map, sizeof(map));
    Session& session = g_session;
    bool hold = false;

    if (!client)
    {
        const uint64_t now = sceKernelGetProcessTime();

        if (!g_awaySince)
            g_awaySince = now;

        if (now - g_awaySince > kAwayUs)
        {
            if (session.active)
            {
                session.active = false;
                g_stop.store(true);
                T7Log_Write("[Share] left the host's lobby");
            }

            if (g_overriding)
            {
                g_overriding = false;
                T7Maps_SetSessionMods(nullptr, -1);
            }

            if (!g_busy.load() && g_phase.load() != kFailed)
                g_phase.store(kIdle);

            g_pendingSince = 0;
            g_heldAt = 0;
            g_keptAt = 0;
        }
    }
    else
    {
        const uint64_t now = sceKernelGetProcessTime();
        g_awaySince = 0;

        if (!session.active || session.hostIp != role.hostIp || strcmp(session.map, map) != 0)
        {
            if (!g_pendingSince || g_pendingHost != role.hostIp || strcmp(g_pendingMap, map) != 0)
            {
                g_pendingSince = now;
                g_pendingHost = role.hostIp;
                snprintf(g_pendingMap, sizeof(g_pendingMap), "%s", map);
            }

            if (g_busy.load())
            {
                g_stop.store(true);
            }
            else if (now - g_pendingSince >= kSettleUs)
            {
                g_pendingSince = 0;
                Start(role, map);
            }

            hold = true;
        }
        else
        {
            g_pendingSince = 0;

            if (frame && !g_busy.load() && g_phase.load() == kInstalling)
                Finish();

            if (!g_busy.load() && g_phase.load() == kFailed && !g_overriding)
            {
                static const char k_none[1][kT7ShareName] = {};
                T7Log_Write("[Share] the host's mods are not known, so none of this console's mods load in its lobby");
                T7Maps_SetSessionMods(k_none, 0);
                g_overriding = true;
            }

            const int phase = g_phase.load();
            hold = g_busy.load() || (phase != kReady && phase != kFailed);
        }

        if (frame)
        {
            ShowNotice(role, now);

            if (hold)
                g_heldAt = now;

            if (hold || (g_heldAt && now - g_heldAt < kLaunchGraceUs && !T7Maps_InLevel()))
                KeepJoinAlive(now);
            else
                g_keptAt = 0;
        }
    }

    g_lastHold.store(hold);
    g_evaluating.store(false);
    return hold;
}

static void Rate(char* out, size_t size, double perSecond)
{
    const double megabytes = perSecond / (1024.0 * 1024.0);

    if (megabytes >= 1024.0)
        snprintf(out, size, "%.1f GB/s", megabytes / 1024.0);
    else if (megabytes >= 10.0)
        snprintf(out, size, "%.0f MB/s", megabytes);
    else if (megabytes >= 1.0)
        snprintf(out, size, "%.1f MB/s", megabytes);
    else
        snprintf(out, size, "%.0f KB/s", perSecond / 1024.0);
}

static void Size(char* out, size_t size, uint64_t bytes)
{
    const double megabytes = (double)bytes / (1024.0 * 1024.0);

    if (megabytes >= 1024.0)
        snprintf(out, size, "%.2f GB", megabytes / 1024.0);
    else if (megabytes >= 1.0)
        snprintf(out, size, "%.0f MB", megabytes);
    else
        snprintf(out, size, "%.0f KB", (double)bytes / 1024.0);
}

static bool ImportBound(const void* function)
{
    SceKernelVirtualQueryInfo info;

    if (sceKernelVirtualQuery((void*)&ImportBound, 0, &info, sizeof(info)) < 0)
        return true;

    const uintptr_t start = (uintptr_t)info.start;
    const uintptr_t end = (uintptr_t)info.end;
    const uintptr_t stub = (uintptr_t)function;

    if (stub < 0x10000)
        return false;

    if (stub < start || stub + 6 > end || ((const uint8_t*)stub)[0] != 0xFF || ((const uint8_t*)stub)[1] != 0x25)
        return true;

    const uintptr_t slot = stub + 6 + (intptr_t)*(const int32_t*)(stub + 2);

    if (!RangeReadable(slot, 8))
        return false;

    const uintptr_t target = *(const uintptr_t*)slot;
    return target >= 0x10000 && (target < start || target >= end);
}

static bool MigrateWindowOpen()
{
    const uint64_t until = g_migrateUntil.load();
    return until && sceKernelGetProcessTime() < until;
}

static uintptr_t MigrateWaitDvar()
{
    const uintptr_t dvar = *(const uintptr_t*)(g_base + kMigrateWaitDvar);

    if (dvar < 0x10000 || !RangeReadable(dvar, kDvarInt + 4))
        return 0;

    const int32_t kind = *(const int32_t*)(dvar + kDvarKind);
    return kind == 6 || kind == 7 ? dvar : 0;
}

static void ExtendMigrateWait()
{
    const uintptr_t dvar = MigrateWaitDvar();

    if (!dvar)
        return;

    const int32_t current = *(const int32_t*)(dvar + kDvarInt);

    if (current >= kMigrateWaitMs)
        return;

    int32_t none = -1;

    if (g_savedMigrateWait.compare_exchange_strong(none, current))
        T7Log_Write("[Share] handover: waiting up to %d ms for the new host instead of %d ms", kMigrateWaitMs, current);

    *(volatile int32_t*)(dvar + kDvarInt) = kMigrateWaitMs;
}

static void RestoreMigrateWait()
{
    if (g_savedMigrateWait.load() < 0 || MigrateWindowOpen())
        return;

    const int32_t saved = g_savedMigrateWait.exchange(-1);
    const uintptr_t dvar = MigrateWaitDvar();

    if (dvar && saved >= 0)
        *(volatile int32_t*)(dvar + kDvarInt) = saved;
}

static void OpenLobbyLog()
{
    g_lobbyLogLines.store(0);
    g_lobbyLogUntil.store(sceKernelGetProcessTime() + kLobbyLogUs);
}

#ifdef _DEBUG
static uint64_t PrintInfo_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0, double x1,
                            double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t until = g_lobbyLogUntil.load();

    if (until && a1 >= 0x10000 && sceKernelGetProcessTime() < until && g_lobbyLogLines.fetch_add(1) < kLobbyLogLines)
    {
        const uint64_t top = *(const uint64_t*)(a1 + 72);
        const uint64_t base = *(const uint64_t*)(a1 + 80);
        const uint64_t text = base + 16 < top && (*(const uint32_t*)(base + 16) & 0xF) == 4 ? *(const uint64_t*)(base + 24) : 0;

        if (text >= 0x10000)
        {
            const uint64_t length = *(const uint64_t*)(text + 8) & 0x3FFFFFFFFFFFFFFFull;
            T7Log_Write("[Lobby] %.*s", (int)(length > 300 ? 300 : length), (const char*)(text + 20));
        }
    }

    return g_printInfo(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}
#endif

static void HandoverWatch()
{
    static uint64_t checkedAt = 0;
    static int32_t lastActive = -1, lastType = -1, lastMigrate = -1, lastHost = -1;
    static uint64_t lastTarget = 0;
    const uint64_t until = g_lobbyLogUntil.load();
    const uint64_t now = sceKernelGetProcessTime();

    if (!until || now >= until || now - checkedAt < 100000)
        return;

    checkedAt = now;
    const uintptr_t client = g_base + kClientSessions + kClientSessionSize * kGameLobby;
    const uintptr_t host = g_base + kHostSessions + kHostSessionSize * kGameLobby;
    const int32_t active = *(const int32_t*)(client + kSessionActive);
    const int32_t type = *(const int32_t*)(client + kSessionHostType);
    const int32_t migrate = *(const int32_t*)(client + kClientMigrateState);
    const uint64_t target = *(const uint64_t*)(client + kClientMigrateTo);
    const int32_t hosting = *(const int32_t*)(host + kSessionActive);

    if (active == lastActive && type == lastType && migrate == lastMigrate && target == lastTarget && hosting == lastHost)
        return;

    lastActive = active;
    lastType = type;
    lastMigrate = migrate;
    lastTarget = target;
    lastHost = hosting;
    T7Log_Write("[Share] handover state: joined lobby %d (host address type %d), migrate %d to %llx; hosted lobby %d", active, type,
                migrate, (unsigned long long)target, hosting);
}

static uint64_t CanMigrate_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0, double x1,
                             double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t allowed = g_canMigrate(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if ((allowed & 0xFF) || a1 < 0x10000 || !MigrateWindowOpen() || *(const int32_t*)(a1 + kSessionActive) != kSessionRunning)
        return allowed;

    const uint64_t until = g_migrateUntil.load();

    if (g_migrateLogged.exchange(until) != until)
        T7Log_Write("[Share] handover: the engine refuses lobby migration in LAN; allowed for this handover");

    return 1;
}

static void LogMembers(const char* why);

static uint64_t CanHost_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0, double x1,
                          double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t allowed = g_canHost(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    if ((allowed & 0xFF) || !MigrateWindowOpen() || *(const int32_t*)(g_base + kNetworkMode) != kNetworkLan)
        return allowed;

    const uint64_t until = g_migrateUntil.load();

    if (g_canHostLogged.exchange(until) != until)
        T7Log_Write("[Share] handover: the engine's online host check (NAT, bandwidth) said no; LAN needs neither, allowed");

    return 1;
}

static uint64_t CanBecomeHost_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0,
                                double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    const uint64_t result = g_canBecomeHost(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
    const int answer = (result & 0xFF) ? 1 : 0;

    if (MigrateWindowOpen() && g_becomeHostLogged.exchange(answer) != answer)
        T7Log_Write("[Share] handover: can this console take over the lobby: %s", answer ? "yes" : "no");

    return result;
}

static uint64_t MigrateMessage_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6, double x0,
                                 double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    if (*(const int32_t*)(g_base + kNetworkMode) == kNetworkLan)
    {
        g_migrateUntil.store(sceKernelGetProcessTime() + kMigrateWindowUs);
        g_becomeHostLogged.store(-1);
        ExtendMigrateWait();
        OpenLobbyLog();
        T7Log_Write("[Share] handover: the LAN host is moving the lobby to another console; following it");
        LogMembers("handover");
    }

    return g_migrateMessage(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);
}

static uintptr_t MemberRecord(uint64_t xuid, int type)
{
    const uintptr_t session = g_base + kClientSessions + kClientSessionSize * kGameLobby;

    for (int i = 0; i < kMemberSlots; ++i)
    {
        const uintptr_t slot = session + kSessionMembers + kMemberStride * (size_t)i;

        if (*(const uint64_t*)slot != xuid)
            continue;

        const uintptr_t data = *(const uintptr_t*)(slot + 8);
        const uintptr_t record = data + kMemberLobby + kMemberLobbyStride * (size_t)type;
        return data >= 0x10000 && RangeReadable(record, kMemberLobbyStride) ? record : 0;
    }

    return 0;
}

static void LogMembers(const char* why)
{
    const uintptr_t session = g_base + kClientSessions + kClientSessionSize * kGameLobby;

    for (int i = 0; i < 4; ++i)
    {
        const uintptr_t user = *(const uintptr_t*)(g_base + kLocalUsers + 8 * (size_t)i);

        if (user >= 0x10000 && RangeReadable(user, 52) && *(const int32_t*)(user + 48) > 0)
            T7Log_Write("[Share] %s: this console's player %d is %llx", why, i, (unsigned long long)*(const uint64_t*)user);
    }

    for (int i = 0; i < kMemberSlots; ++i)
    {
        const uintptr_t slot = session + kSessionMembers + kMemberStride * (size_t)i;
        const uint64_t xuid = *(const uint64_t*)slot;

        if (!xuid)
            continue;

        const uintptr_t record = MemberRecord(xuid, (int)kGameLobby);

        if (!record)
        {
            T7Log_Write("[Share] %s: member %llx has no lobby record", why, (unsigned long long)xuid);
            continue;
        }

        const uint8_t* const address = (const uint8_t*)(record + kMemberAddress);
        T7Log_Write("[Share] %s: member %llx netsrc %d, address %02X%02X%02X%02X%02X%02X%02X%02X", why, (unsigned long long)xuid,
                    *(const int32_t*)(record + kMemberNetsrc), address[0], address[1], address[2], address[3], address[4],
                    address[5], address[6], address[7]);
    }
}

static bool CanHost(uint64_t xuid)
{
    const uintptr_t record = MemberRecord(xuid, (int)kGameLobby);
    return !record || *(const int32_t*)(record + kMemberNetsrc) != kNetsrcNoAddress;
}

static bool Promote(const char* text)
{
    char* at = nullptr;
    const long controller = strtol(text, &at, 10);

    if (!at || *at != '|')
        return false;

    const long type = strtol(at + 1, &at, 10);

    if (!at || *at != '|')
        return false;

    const uint64_t xuid = strtoull(at + 1, nullptr, 16);
    const Role role = ReadRole();

    if (!g_canMigrate || !g_migrateMessage)
    {
        T7Log_Write("[Share] handover: not available, the engine hooks did not install");
        return false;
    }

    if (!xuid || !role.lan || !role.host || controller < 0 || controller > 3 || type < 0 || type > 1)
    {
        T7Log_Write("[Share] handover refused: player %llx, controller %ld, lobby type %ld, LAN %d, host %d",
                    (unsigned long long)xuid, controller, type, role.lan ? 1 : 0, role.host ? 1 : 0);
        return false;
    }

    const uintptr_t session = g_base + kHostSessions + kHostSessionSize * (size_t)type;
    const int32_t state = *(const int32_t*)(session + kSessionActive);

    if (state != kSessionRunning)
    {
        T7Log_Write("[Share] handover refused: this console hosts no running lobby of type %ld (state %d)", type, state);
        return false;
    }

    LogMembers("handover");

    if (!CanHost(xuid))
    {
        T7Log_Write("[Share] handover refused: the game has no network address for %llx, so nobody could follow", (unsigned long long)xuid);
        return false;
    }

    g_migrateUntil.store(sceKernelGetProcessTime() + kMigrateWindowUs);
    g_becomeHostLogged.store(-1);
    ExtendMigrateWait();
    OpenLobbyLog();
    T7Log_Write("[Share] handover: making %llx the host of this LAN lobby", (unsigned long long)xuid);
    ((void (*)(uintptr_t, int, uint64_t))(g_base + kMigrateTo))(session, (int)controller, xuid);
    return true;
}
}

void T7Share_Install(uintptr_t base)
{
    using namespace T7Share;

    static bool installed = false;

    if (installed || !base)
        return;

    installed = true;
    g_base = base;

    if (!RangeReadable(base + kNetworkMode, 4) || !RangeReadable(base + kClientSessions, kClientSessionSize * 2) ||
        !RangeReadable(base + kTaskList, sizeof(uintptr_t)))
    {
        T7Log_Write("[Share] off: the lobby data is not where this build expects it");
        return;
    }

    if (!ImportBound((const void*)&sceNetSocket) || !ImportBound((const void*)&sceNetConnect) ||
        !ImportBound((const void*)&sceNetAccept) || !ImportBound((const void*)&sceNetRecv) ||
        !ImportBound((const void*)&sceNetSend))
    {
        T7Log_Write("[Share] off: the network library is not loaded in this process");
        return;
    }

    for (UdpPeer& peer : g_peers)
    {
        peer.fd = -1;
        peer.fdId = -1;
    }

    g_asks = (Ask*)Take(sizeof(Ask) * kUdpAsks);
    ScePthread asks;

    if (g_asks && ImportBound((const void*)&sceNetSendto) &&
        scePthreadCreate(&asks, nullptr, Asks, nullptr, "BO3-Customs Share Game") == 0)
    {
        scePthreadDetach(asks);
        Detour_Attach(&g_dwReceiveDetour, (uint64_t)(base + kDwReceive), (void*)DwReceive_h, (void**)&g_dwReceive);
        g_udpReady = g_dwReceive != nullptr;
    }

    if (!g_udpReady)
        T7Log_Write("[Share] downloads through the game's connection are off; only the direct share works");

    if (RangeReadable(base + kHostSessions, kHostSessionSize * 2))
    {
        Detour_Attach(&g_canMigrateDetour, (uint64_t)(base + kCanMigrate), (void*)CanMigrate_h, (void**)&g_canMigrate);
        Detour_Attach(&g_migrateMessageDetour, (uint64_t)(base + kMigrateMessage), (void*)MigrateMessage_h,
                      (void**)&g_migrateMessage);
        Detour_Attach(&g_canHostDetour, (uint64_t)(base + kCanHost), (void*)CanHost_h, (void**)&g_canHost);
        Detour_Attach(&g_canBecomeHostDetour, (uint64_t)(base + kCanBecomeHost), (void*)CanBecomeHost_h,
                      (void**)&g_canBecomeHost);
    }

    if (!g_canMigrate || !g_migrateMessage || !g_canHost || !g_canBecomeHost)
        T7Log_Write("[Share] promote to host is off: the lobby migration hooks did not install");

#ifdef _DEBUG
    Detour_Attach(&g_printInfoDetour, (uint64_t)(base + kPrintInfo), (void*)PrintInfo_h, (void**)&g_printInfo);
#endif

    ScePthread thread;

    if (scePthreadCreate(&thread, nullptr, Server, nullptr, "BO3-Customs Share") != 0)
    {
        T7Log_Write("[Share] off: the server thread did not start");
        return;
    }

    scePthreadDetach(thread);
    g_ready = true;
}

void T7Share_Tick()
{
    if (T7Share::g_ready)
    {
        T7Share::Evaluate(true);
        T7Share::HandoverWatch();
        T7Share::RestoreMigrateWait();
    }
}

static uint64_t PlayerKey(const char* name)
{
    while (*name == ' ')
        ++name;

    uint64_t key = 14695981039346656037ull;
    bool any = false;

    for (const char* c = name; *c; ++c)
    {
        key = (key ^ (uint8_t)(*c >= 'A' && *c <= 'Z' ? *c + 32 : *c)) * 1099511628211ull;
        any = true;
    }

    return any ? key : 0;
}

static int Lacking(const char* map, uint64_t xuid)
{
    using namespace T7Share;

    const uint32_t hash = NameHash(map);
    const uint64_t now = sceKernelGetProcessTime();
    const uint64_t mine = g_myXuid.load();
    int missing = 0;

    Spin lock(g_mapsLock);

    for (const MapsMember& slot : g_mapsMembers)
    {
        if (!slot.xuid || !slot.known || now - slot.seenAt > kMapsStaleUs || slot.xuid == mine || (xuid && slot.xuid != xuid))
            continue;

        bool have = false;

        for (int i = 0; i < slot.count && !have; ++i)
            have = slot.hashes[i] == hash;

        missing += have ? 0 : 1;
    }

    return missing;
}

static int CardFor(const char* map, uint64_t key)
{
    using namespace T7Share;

    const Role role = ReadRole();

    if (!key || !role.lan || !map[0])
        return -1;

    if (T7Maps_EngineHasMap(map))
        return -2;

    if (role.host)
    {
        if (Lacking(map, key) <= 0)
            return 0;

        const int progress = ProgressOf(map, key, true);
        return progress >= 0 ? 100 + progress : 1;
    }

    const uint64_t now = sceKernelGetProcessTime();
    Spin lock(g_lacksLock);

    if (!g_lacksAt || now - g_lacksAt > kMapsStaleUs)
        return 0;

    bool lacking = false;

    for (int i = 0; i < g_lacksCount && !lacking; ++i)
        lacking = g_lacksKeys[i] == key;

    if (!lacking)
        return 0;

    for (int i = 0; i < g_progressCount && now - g_progressAt <= kMapsStaleUs; ++i)
    {
        if (g_progress[i].key == key && g_progress[i].map == g_lacksMap && g_progress[i].percent >= 0)
            return 100 + g_progress[i].percent;
    }

    return 1;
}

float T7Share_Query(const char* what)
{
    using namespace T7Share;

    if (what && strncmp(what, "pausemenu:", 10) == 0)
    {
        T7Kbm_SetPauseMenu(what[10] == '1');
        T7Log_Write("[KBM] pause menu %s", what[10] == '1' ? "open" : "closed");
        return 1.0f;
    }

    if (!g_ready || !what)
        return 0.0f;

    if (strcmp(what, "hold") == 0)
        return g_wrapped.load() && Evaluate(false) ? 1.0f : 0.0f;

    if (strcmp(what, "wrapped") == 0)
    {
        if (!g_wrapped.exchange(true))
            T7Log_Write("[Share] the lobby's launch step now waits for downloads");

        return 1.0f;
    }

    if (strcmp(what, "plain") == 0)
        g_wrapped.store(false);

    if (strncmp(what, "me:", 3) == 0)
    {
        const uint64_t key = PlayerKey(what + 3);

        if (key && g_myXuid.exchange(key) != key)
            T7Log_Write("[Share] this console tells LAN hosts which custom maps it has, as \"%s\"", what + 3);

        return key ? 1.0f : 0.0f;
    }

    if (strcmp(what, "lanhost") == 0)
    {
        const Role role = ReadRole();
        return role.lan && role.host ? 1.0f : 0.0f;
    }

    if (strcmp(what, "lan") == 0)
    {
        const Role role = ReadRole();
        return role.lan && (role.host || role.client) ? 1.0f : 0.0f;
    }

    if (strncmp(what, "stock:", 6) == 0)
        return T7Maps_EngineHasMap(what + 6) ? 1.0f : 0.0f;

    if (strcmp(what, "mapsversion") == 0)
        return (float)(g_mapsVersion.load() % 1000000u);

    if (strncmp(what, "custom:", 7) == 0)
        return T7Maps_HasMap(what + 7) ? 1.0f : 0.0f;

    if (strncmp(what, "card:", 5) == 0)
    {
        char hint[kT7ShareName];
        const char* const bar = strchr(what + 5, '|');

        if (!bar || (size_t)(bar - (what + 5)) >= sizeof(hint))
            return -1.0f;

        snprintf(hint, sizeof(hint), "%.*s", (int)(bar - (what + 5)), what + 5);
        char map[kT7ShareName];

        if (!ReadMapName(map, sizeof(map)))
            snprintf(map, sizeof(map), "%s", hint);

        return (float)CardFor(map, PlayerKey(bar + 1));
    }

    if (strncmp(what, "promote:", 8) == 0)
        return Promote(what + 8) ? 1.0f : 0.0f;

    if (strncmp(what, "canhost:", 8) == 0)
    {
        const uint64_t xuid = strtoull(what + 8, nullptr, 16);
        return xuid && CanHost(xuid) ? 1.0f : 0.0f;
    }

    if (strncmp(what, "lacks:", 6) == 0)
    {
        char map[kT7ShareName];
        const char* const bar = strchr(what + 6, '|');

        if (!bar || (size_t)(bar - (what + 6)) >= sizeof(map))
            return 0.0f;

        snprintf(map, sizeof(map), "%.*s", (int)(bar - (what + 6)), what + 6);
        const uint64_t key = PlayerKey(bar + 1);

        if (!key)
            return 0.0f;

        if (ReadRole().host)
            return Lacking(map, key) > 0 ? 1.0f : 0.0f;

        return ToldLacking(map, key) ? 1.0f : 0.0f;
    }

    if (strncmp(what, "ban:", 4) == 0 || strncmp(what, "banned:", 7) == 0)
    {
        const bool adding = what[1] == 'a' && what[2] == 'n' && what[3] == ':';
        const char* const text = what + (adding ? 4 : 7);
        const char* const bar = strchr(text, '|');
        char id[64];

        if (!bar || (size_t)(bar - text) >= sizeof(id))
            return 0.0f;

        snprintf(id, sizeof(id), "%.*s", (int)(bar - text), text);
        const uint64_t keys[2] = { PlayerKey(id), PlayerKey(bar + 1) };
        Spin lock(g_banLock);

        for (int i = 0; i < g_banCount; ++i)
        {
            for (const uint64_t key : keys)
            {
                if (key && g_bans[i] == key)
                    return 1.0f;
            }
        }

        if (!adding || g_banCount + 2 > (int)(sizeof(g_bans) / sizeof(g_bans[0])))
            return 0.0f;

        g_bans[g_banCount++] = keys[0];
        g_bans[g_banCount++] = keys[1];
        T7Log_Write("[Share] banned \"%s\" from this lobby", bar + 1);
        return 1.0f;
    }

    if (strncmp(what, "progress:", 9) == 0)
    {
        char map[kT7ShareName];
        const char* const bar = strchr(what + 9, '|');

        if (!bar || (size_t)(bar - (what + 9)) >= sizeof(map))
            return -1.0f;

        snprintf(map, sizeof(map), "%.*s", (int)(bar - (what + 9)), what + 9);
        const uint64_t key = PlayerKey(bar + 1);
        return key ? (float)ProgressOf(map, key, ReadRole().host) : -1.0f;
    }

    if (strncmp(what, "lacking:", 8) == 0)
        return (float)Lacking(what + 8, 0);

    return 0.0f;
}

bool T7Share_Wants(const char* map)
{
    using namespace T7Share;

    if (!g_ready || !map || !g_wrapped.load())
        return false;

    Evaluate(false);

    const Role role = ReadRole();

    if (!role.client || T7Maps_HasMap(map) || T7Maps_EngineHasMap(map))
        return false;

    const Session& session = g_session;

    if (session.active && session.hostIp == role.hostIp && EqualsNoCase(session.map, map))
        return session.needMap && g_phase.load() != kFailed;

    return true;
}

void T7Share_LobbyState(uintptr_t L)
{
    using namespace T7Share;

    if (!g_ready || !L || T7Lua_IsUiState(L) || !ReadRole().client)
        return;

    const uint64_t now = sceKernelGetProcessTime();

    if (g_wrapTriedAt && now - g_wrapTriedAt < kWrapEveryUs)
        return;

    g_wrapTriedAt = now;
    T7Lua_RunShareVm(L);
}

bool T7Share_Status(T7ShareStatus* status)
{
    using namespace T7Share;

    static uint64_t lastAt = 0;
    static uint64_t lastReceived = 0;
    static uint64_t lastWriteBytes = 0;
    static uint64_t lastWriteUs = 0;
    static double   speed = 0.0;
    static double   writing = 0.0;

    memset(status, 0, sizeof(*status));

    if (!g_ready)
        return false;

    const int phase = g_phase.load();
    const uint64_t now = sceKernelGetProcessTime();

    Spin lock(g_textLock);
    const Session& session = g_session;

    if (phase != kDownloading)
    {
        lastAt = 0;
        speed = 0.0;
        writing = 0.0;
    }

    if (phase == kFailed)
    {
        const uint64_t at = g_failedAt.load();

        if ((!session.needMap && session.newMods == 0) || !at || now - at > kFailureShownUs || now - at < kNoticeDelayUs ||
            !StillOn(session.map))
            return false;

        snprintf(status->title, sizeof(status->title), "%s", "COULD NOT DOWNLOAD FROM THE HOST");
        snprintf(status->name, sizeof(status->name), "%s", session.needMap ? session.map : "the host's mods");
        snprintf(status->detail, sizeof(status->detail), "%s", session.error);
        status->failed = true;
        return true;
    }

    if (!session.active || phase == kIdle || phase == kReady)
        return false;

    if (!session.needMap && session.newMods == 0)
        return false;

    snprintf(status->title, sizeof(status->title), "%s", "DOWNLOADING FROM THE HOST");

    if (session.needMap && session.newMods > 0)
        snprintf(status->name, sizeof(status->name), "%s and %d mod%s", session.map, session.newMods,
                 session.newMods == 1 ? "" : "s");
    else if (session.needMap)
        snprintf(status->name, sizeof(status->name), "%s", session.map);
    else
        snprintf(status->name, sizeof(status->name), "%d mod%s", session.newMods, session.newMods == 1 ? "" : "s");

    if (phase == kContacting || phase == kListing)
    {
        status->name[0] = 0;
        snprintf(status->detail, sizeof(status->detail), "%s", "Asking the host for its files...");
        return true;
    }

    const uint64_t total = g_total.load();
    const uint64_t done = g_done.load();

    if (phase == kInstalling)
    {
        snprintf(status->detail, sizeof(status->detail), "%s", "Installing...");
        status->fraction = 1.0f;
        return true;
    }

    const uint64_t received = g_received.load();
    const uint64_t writeBytes = g_writeBytes.load();
    const uint64_t writeUs = g_writeUs.load();

    const double weight = lastAt && now > lastAt ? (double)(now - lastAt) / ((double)(now - lastAt) + kRateSmoothUs) : 0.0;

    if (lastAt && now > lastAt && received >= lastReceived)
    {
        const double instant = (double)(received - lastReceived) * 1000000.0 / (double)(now - lastAt);
        speed = speed > 0.0 ? speed + (instant - speed) * weight : instant;
    }

    if (lastAt && writeUs > lastWriteUs && writeBytes >= lastWriteBytes)
    {
        const double instant = (double)(writeBytes - lastWriteBytes) * 1000000.0 / (double)(writeUs - lastWriteUs);
        writing = writing > 0.0 ? writing + (instant - writing) * weight : instant;
    }

    lastAt = now;
    lastReceived = received;
    lastWriteBytes = writeBytes;
    lastWriteUs = writeUs;

    char have[24];
    char all[24];
    char rate[24];
    Size(have, sizeof(have), done);
    Size(all, sizeof(all), total);

    if (speed >= 1024.0 && total > done)
    {
        const uint64_t seconds = (uint64_t)((double)(total - done) / speed);

        if (seconds >= 90)
            snprintf(status->detail, sizeof(status->detail), "%s of %s  -  about %d min left", have, all, (int)((seconds + 30) / 60));
        else
            snprintf(status->detail, sizeof(status->detail), "%s of %s  -  about %d sec left", have, all, (int)seconds);
    }
    else
    {
        snprintf(status->detail, sizeof(status->detail), "%s of %s", have, all);
    }

    size_t used = 0;
    const struct
    {
        const char* label;
        double      value;
    } speeds[] = {
        { "Host read", (double)g_hostRead.load() },
        { "Host upload", (double)g_hostUpload.load() },
        { "Download", speed },
        { "Write", writing },
    };

    for (const auto& shown : speeds)
    {
        if (shown.value < 1024.0 || used >= sizeof(status->speeds))
            continue;

        Rate(rate, sizeof(rate), shown.value);
        used += (size_t)snprintf(status->speeds + used, sizeof(status->speeds) - used, "%s%s %s", used ? "  -  " : "", shown.label, rate);
    }

    snprintf(status->file, sizeof(status->file), "File %d of %d: %s", g_fileAt.load(), g_fileCount.load(),
             session.current);
    status->fraction = total ? (float)((double)done / (double)total) : 0.0f;
    return true;
}

void T7Share_Cancel()
{
    using namespace T7Share;

    if (!g_ready || !g_session.active)
        return;

    const int phase = g_phase.load();

    if (phase == kReady || phase == kFailed || phase == kIdle)
        return;

    T7Log_Write("[Share] the player cancelled the download");
    g_stop.store(true);

    if (!g_busy.load())
        Fail("the download was cancelled");
}

bool T7Share_TakeMapsChanged()
{
    return T7Share::g_mapsChanged.exchange(false);
}
