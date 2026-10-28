#include "headers.hpp"
#include "t7_mapimages.hpp"
#include "t7_maps.hpp"
#include "t7_log.hpp"
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
    uint64_t retryAfter;
    bool failureLogged;
};

static uintptr_t g_base = 0;
static Picture   g_pictures[kMaxPictures];
static int       g_pictureCount = 0;

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

static uint8_t* ReadWholeFile(const char* path, size_t* outSize)
{
    SceKernelStat st;

    if (sceKernelStat(path, &st) != 0 || st.st_size <= 0 || (size_t)st.st_size > kMaxFileBytes)
        return nullptr;

    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);

    if (fd < 0)
        return nullptr;

    const size_t size = (size_t)st.st_size;
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
        free(data);
        return nullptr;
    }

    *outSize = size;
    return data;
}

static uint8_t* PixelMemory(uint64_t image)
{
    if (!image || image > UINTPTR_MAX - (kImagePixels + sizeof(uint64_t)) ||
        !RangeReadable((uintptr_t)image + kImagePixels, sizeof(uint64_t)) ||
        g_base > UINTPTR_MAX - kGpuHeap || !RangeReadable(g_base + kGpuHeap, sizeof(uint64_t)))
        return nullptr;

    uint64_t pixels = *(uint64_t*)(image + kImagePixels);

    if (pixels == (uint64_t)-1)
        return nullptr;

    const uint64_t heap = *(uint64_t*)(g_base + kGpuHeap);

    if (heap && pixels >= heap && pixels - heap < 16777224)
    {
        if (pixels > UINTPTR_MAX - 16 ||
            g_base > UINTPTR_MAX - kGpuHeapTable || !RangeReadable(g_base + kGpuHeapTable, sizeof(uint64_t)))
            return nullptr;

        const uint64_t index = (*(uint64_t*)(pixels + 8) >> 2) & 0x7FFFFFFF;
        const uint64_t table = *(uint64_t*)(g_base + kGpuHeapTable);

        if (!index || !table || table > UINTPTR_MAX - 16)
            return nullptr;

        const uint64_t tableBase = *(uint64_t*)(table + 8);

        if (index > (UINT64_MAX - tableBase) / 16)
            return nullptr;

        const uint64_t resolved = tableBase + 16 * index;
        pixels = resolved >= 16 ? resolved - 16 : 0;
    }

    return (uint8_t*)pixels;
}

static bool WritePicture(const PngImage& picture, uint8_t* pixels, int width, int height, int pitch)
{
    const int64_t across = (int64_t)picture.width * height;
    const int64_t down = (int64_t)picture.height * width;

    memset(pixels, 0, (size_t)pitch * 4 * (size_t)height);

    if (across * 10 >= down * 9 && across * 9 <= down * 10)
        return Png_Scale(picture, pixels, width, height, (size_t)pitch * 4, nullptr);

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
                     (size_t)pitch * 4, nullptr);
}

static void ReleaseImage(uint64_t image)
{
    if (!image || image > UINTPTR_MAX - (kSlotReleased + sizeof(uint64_t)) ||
        !RangeReadable((uintptr_t)image + kSlotName, 64) || !RangeReadable((uintptr_t)image + kSlotInUse, 1) ||
        !RangeReadable((uintptr_t)image + kSlotReleased, sizeof(uint64_t)) ||
        g_base > UINTPTR_MAX - kImageFrame || !RangeReadable(g_base + kImageFrame, sizeof(uint64_t)))
        return;

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

static uint64_t MakePicture(const char* name, const char** failure, bool logFailure)
{
    if (failure)
        *failure = nullptr;

    const bool mod = strncmp(name, T7_MOD_PREVIEW_PREFIX, sizeof(T7_MOD_PREVIEW_PREFIX) - 1) == 0;
    const bool preview = mod || strncmp(name, T7_USERMAP_PREVIEW_PREFIX, sizeof(T7_USERMAP_PREVIEW_PREFIX) - 1) == 0;
    const char* const map = name + (preview ? sizeof(T7_USERMAP_PREVIEW_PREFIX) : sizeof(T7_USERMAP_LOADING_PREFIX)) - 1;
    char previewFile[288] = {};
    const bool hasPreview = preview && !mod && T7Maps_PreviewFile(map, previewFile, sizeof(previewFile));
    const char* const file = preview ? (hasPreview ? previewFile : "previewimage.png") : "loadingimage.png";
    const int width = preview ? kPreviewWidth : kLoadingWidth;
    const int height = preview ? kPreviewHeight : kLoadingHeight;

    char path[320];

    if (mod ? !ModPicturePath(name, path, sizeof(path)) : !IsMapToken(map) || !T7Maps_MapFile(map, file, path, sizeof(path)))
    {
        if (failure)
            *failure = "preview PNG not indexed in the installed map/mod folder";
        return 0;
    }

    size_t size = 0;
    uint8_t* const png = ReadWholeFile(path, &size);

    if (!png)
    {
        if (failure)
            *failure = "preview PNG could not be read or exceeded the size limit";
        return 0;
    }

    PngImage picture;

    const char* decodeFailure = nullptr;

    if (!Png_Open(png, size, &picture, &decodeFailure))
    {
        free(png);
        if (failure)
            *failure = decodeFailure ? decodeFailure : "PNG decoder rejected the image without a reason";
        return 0;
    }

    const uint64_t image = ((DynamicImage_t)(g_base + kDynamicImage))((uint64_t)width, (uint64_t)height, kPixelFormat,
                                                                      kImageFlags, name);

    if (!image)
    {
        free(png);
        if (failure)
            *failure = "dynamic image allocation failed";
        return 0;
    }

    if (image > UINTPTR_MAX - (kSlotReleased + sizeof(uint64_t)) ||
        !RangeReadable((uintptr_t)image, kSlotReleased + sizeof(uint64_t)))
    {
        free(png);
        if (failure)
            *failure = "dynamic image object is outside readable memory";
        return 0;
    }

    const uint32_t reg4 = *(uint32_t*)(image + kImageTextureRegs + 16);
    const int pitch = (int)((reg4 >> 13) & 0x3FFF) + 1;
    const uint32_t bytes = *(uint32_t*)(image + kImagePixelBytes);
    uint8_t* const pixels = PixelMemory(image);
    const int imageWidth = *(uint16_t*)(image + kImageWidth);
    const int imageHeight = *(uint16_t*)(image + kImageHeight);

    const uint64_t surfaceBytes = pitch > 0 ? (uint64_t)pitch * 4 * (uint64_t)height : 0;

    // The native allocator owns this GPU surface. Validate its descriptor and
    // capacity; a CPU-readable virtual-memory query is not its allocation contract.
    if (!pixels || imageWidth != width || imageHeight != height || pitch < width || pitch > width + 64 ||
        surfaceBytes > bytes || surfaceBytes > SIZE_MAX)
    {
        SceKernelVirtualQueryInfo region{};
        const int query = pixels ? sceKernelVirtualQuery(pixels, 0, &region, sizeof(region)) : -1;
        if (logFailure)
            T7Log_Write("[MapImages] surface %s: image=%p pixels=%p dimensions=%dx%d expected=%dx%d pitch=%d bytes=%u need=%llu query=%d protection=0x%x region=%p-%p",
                        name, (void*)image, pixels, imageWidth, imageHeight, width, height, pitch, bytes,
                        (unsigned long long)surfaceBytes, query, region.protection, region.start, region.end);
        free(png);
        ReleaseImage(image);
        if (failure)
            *failure = "dynamic image pixel surface failed validation";
        return 0;
    }

    const bool written = WritePicture(picture, pixels, width, height, pitch);
    free(png);

    if (!written)
    {
        ReleaseImage(image);
        if (failure)
            *failure = "PNG decode or pixel copy failed";
        return 0;
    }

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
    return picture.image && picture.image <= UINTPTR_MAX - (kSlotName + 64) &&
           RangeReadable((uintptr_t)picture.image + kSlotName, 64) &&
           RangeReadable((uintptr_t)picture.image + kSlotInUse, 1) && *(uint8_t*)(picture.image + kSlotInUse) &&
           strncmp((const char*)(picture.image + kSlotName), picture.name, 63) == 0;
}

static uint64_t RetryPicture(Picture& picture)
{
    if (picture.image)
        return picture.image;

    const uint64_t now = sceKernelGetProcessTime();

    if (picture.retryAfter && now < picture.retryAfter)
        return 0;

    const char* failure = nullptr;
    picture.image = MakePicture(picture.name, &failure, !picture.failureLogged);

    if (picture.image)
    {
        picture.retryAfter = 0;

        if (picture.failureLogged)
            T7Log_Write("[MapImages] recovered %s", picture.name);

        picture.failureLogged = false;
        return picture.image;
    }

    picture.retryAfter = now + 2000000;

    if (!picture.failureLogged)
    {
        T7Log_Write("[MapImages] %s unavailable: %s", picture.name, failure ? failure : "unknown image error");
        picture.failureLogged = true;
    }

    return 0;
}

static uint64_t PictureFor(const char* name)
{
    for (int i = 0; i < g_pictureCount; ++i)
    {
        Picture& picture = g_pictures[i];

        if (strcmp(picture.name, name) != 0)
            continue;

        if (picture.image && !StillOurs(picture))
        {
            picture.image = 0;
            picture.retryAfter = 0;
        }

        return RetryPicture(picture);
    }

    if (g_pictureCount >= kMaxPictures || strlen(name) >= sizeof(g_pictures[0].name))
        return 0;

    Picture& picture = g_pictures[g_pictureCount];
    snprintf(picture.name, sizeof(picture.name), "%s", name);
    picture.image = 0;
    picture.retryAfter = 0;
    picture.failureLogged = false;
    ++g_pictureCount;
    return RetryPicture(picture);
}

static const char* StringArgument(uint64_t L, int index)
{
    if (!L || index < 0 || L > UINTPTR_MAX - kStateTop || !RangeReadable((uintptr_t)L + kStateBase, 8) ||
        !RangeReadable((uintptr_t)L + kStateTop, 8))
        return nullptr;

    const uint64_t base = *(uint64_t*)(L + kStateBase);
    const uint64_t top = *(uint64_t*)(L + kStateTop);

    if (base > UINT64_MAX - 16 * (uint64_t)index)
        return nullptr;

    const uint64_t at = base + 16 * (uint64_t)index;

    if (at >= top || top - at < 16 || !RangeReadable((uintptr_t)at, 16) || (*(uint32_t*)at & 0xF) != kTypeString)
        return nullptr;

    const uint64_t string = *(uint64_t*)(at + 8);

    if (!string || string > UINTPTR_MAX - 20 || !RangeReadable((uintptr_t)string + 8, 8))
        return nullptr;

    const uint64_t length = *(uint64_t*)(string + 8) & 0x3FFFFFFFFFFFFFFFull;

    if (length >= 256 || string > UINTPTR_MAX - 20)
        return nullptr;

    const uintptr_t text = (uintptr_t)string + 20;

    if (length + 1 > UINTPTR_MAX - text || !RangeReadable(text, (size_t)length + 1))
        return nullptr;

    return (const char*)text;
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
            if (a1 > UINTPTR_MAX - kStateTop || !RangeReadable((uintptr_t)a1 + kStateTop, 8))
                return ((Hook_t)g_registerOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

            uint64_t top = *(uint64_t*)(a1 + kStateTop);

            if (top > UINTPTR_MAX - 16 || !RangeReadable((uintptr_t)top, 16))
                return ((Hook_t)g_registerOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

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
    if (!L || L > UINTPTR_MAX - kStateTop || !RangeReadable((uintptr_t)L + kStateBase, 8) ||
        !RangeReadable((uintptr_t)L + kStateTop, 8))
        return 0;

    const uint64_t at = *(uint64_t*)(L + kStateBase);
    const uint64_t top = *(uint64_t*)(L + kStateTop);

    if (at >= top || top - at < 16 || !RangeReadable((uintptr_t)at, 16))
        return 0;

    const uint32_t type = *(uint32_t*)at & 0xF;
    uint64_t handle = 0;

    if (type == kTypeLightUserdata)
        handle = *(uint64_t*)(at + 8);
    else if (type == kTypeUserdata && *(uint64_t*)(at + 8))
    {
        const uint64_t userdata = *(uint64_t*)(at + 8);

        if (userdata <= UINTPTR_MAX - 32)
            handle = userdata + 32;
    }

    if (!handle || handle > UINTPTR_MAX - 4 || !RangeReadable((uintptr_t)handle, 4))
        return 0;

    const uint16_t index = *(uint16_t*)handle;

    if (index >= kMaxElements || g_base > UINTPTR_MAX - kGenerations - 2 * (uint64_t)index)
        return 0;

    const uint64_t generation = g_base + kGenerations + 2 * (uint64_t)index;

    if (!RangeReadable((uintptr_t)generation, 2) ||
        *(uint16_t*)(handle + 2) != *(uint16_t*)generation)
        return 0;

    const uint64_t elementOffset = kElements + 408 * (uint64_t)index;

    if (g_base > UINTPTR_MAX - elementOffset - 408)
        return 0;

    const uint64_t element = g_base + elementOffset;

    return RangeReadable((uintptr_t)element, 408) ? element : 0;
}

static uint64_t SetImage_h(uint64_t a1, uint64_t a2, uint64_t a3, uint64_t a4, uint64_t a5, uint64_t a6,
                           double x0, double x1, double x2, double x3, double x4, double x5, double x6, double x7)
{
    if (!a1 || a1 > UINTPTR_MAX - kStateTop || !RangeReadable((uintptr_t)a1 + kStateBase, 8) ||
        !RangeReadable((uintptr_t)a1 + kStateTop, 8))
        return ((Hook_t)g_setImageOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    const uint64_t base = *(uint64_t*)(a1 + kStateBase);
    const uint64_t top = *(uint64_t*)(a1 + kStateTop);

    if (base > UINT64_MAX - 32)
        return ((Hook_t)g_setImageOriginal)(a1, a2, a3, a4, a5, a6, x0, x1, x2, x3, x4, x5, x6, x7);

    const uint64_t at = base + 16;

    if (at < top && top - at >= 16 && RangeReadable((uintptr_t)at, 16) &&
        (*(uint32_t*)at & 0xF) == kTypeLightUserdata &&
        IsPicture(*(uint64_t*)(at + 8)))
    {
        const uint64_t image = *(uint64_t*)(at + 8);
        const uint64_t element = ElementArgument(a1);

        if (element && image <= UINTPTR_MAX - kImageStreamed && RangeReadable((uintptr_t)image + kImageStreamed, 1) &&
            !*(uint8_t*)(image + kImageStreamed))
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

void T7MapImages_Install(uintptr_t base)
{
    using namespace T7MapImages;

    static bool installed = false;

    if (installed)
        return;

    if (!base)
    {
        T7Log_Write("[MapImages] install skipped: game base is null");
        return;
    }

    const bool registerMatch = Matches(base, kRegisterImage, k_registerImage, sizeof(k_registerImage));
    const bool setMatch = Matches(base, kSetImage, k_setImage, sizeof(k_setImage));
    const bool dynamicMatch = Matches(base, kDynamicImage, k_dynamicImage, sizeof(k_dynamicImage));
    const bool findMatch = Matches(base, kFindAsset, k_findAsset, sizeof(k_findAsset));

    if (!registerMatch || !setMatch || !dynamicMatch || !findMatch)
    {
        T7Log_Write("[MapImages] hook signatures rejected: register=%d set=%d dynamic=%d findAsset=%d; custom map icons disabled",
                    registerMatch, setMatch, dynamicMatch, findMatch);
        return;
    }

    g_base = base;

    if (!g_setImageOriginal)
        Detour_Attach(&g_setImageDetour, (uint64_t)(base + kSetImage), (void*)SetImage_h, &g_setImageOriginal);

    if (!g_setImageOriginal)
    {
        T7Log_Write("[MapImages] SetImage hook failed; custom map icons disabled");
        return;
    }

    if (!g_registerOriginal)
        Detour_Attach(&g_registerDetour, (uint64_t)(base + kRegisterImage), (void*)RegisterImage_h, &g_registerOriginal);

    if (!g_registerOriginal)
    {
        T7Log_Write("[MapImages] RegisterImage hook failed; custom map icons disabled");
        return;
    }

    installed = true;
    T7Log_Write("[MapImages] custom usermap/mod preview hooks installed");
}
