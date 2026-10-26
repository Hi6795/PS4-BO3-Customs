#!/usr/bin/env python3
"""Exercise custom-map thumbnail retries and registry-safe preview lookup."""
from pathlib import Path
import os
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
images = (root / "t7_mapimages.cpp").read_text(encoding="utf-8")
maps = (root / "t7_maps.cpp").read_text(encoding="utf-8")
header = (root / "t7_mapimages.hpp").read_text(encoding="utf-8")
maps_header = (root / "t7_maps.hpp").read_text(encoding="utf-8")

start = images.index("static uint64_t PictureFor(const char* name)")
end = images.index("\nstatic const char* StringArgument(", start)
picture_for = images[start:end]

start = images.index("void T7MapImages_RefreshMisses()")
end = images.index("\n", images.index("}", start))
refresh = images[start:end]

start = maps.index("bool T7Maps_MapPreviewPath(")
end = maps.index("\nbool T7Maps_ModPreviewFile(", start)
preview_path = maps[start:end]

start = maps.index("static void FindPreview(Package& package, const char* json)")
end = maps.index("\nstatic bool AllDigits(", start)
find_preview = maps[start:end]

assert "void T7MapImages_RefreshMisses();" in header
assert "bool T7Maps_MapPreviewPath(const char* map, char* path, size_t size);" in maps_header
assert "T7Maps_MapPreviewPath(map, path, sizeof(path))" in images
assert "T7Maps_PreviewFile(map)" not in images
assert "T7MapImages_RefreshMisses();" in maps[maps.index("bool T7Maps_Refresh()"):maps.index("bool T7Maps_Uninstall(")]
assert "[MapImages]" in images
assert '"[MapImages] hooks installed; custom preview loading enabled"' in images
assert '"[MapImages] DISABLED: register-image signature mismatch"' in images
assert '"[MapImages] DISABLED: set-image detour failed"' in images
assert 'LogImageFailure(name, reason ? reason : "PNG header rejected", path)' in images
assert 'LogImageFailure(name, reason ? reason : "PNG decoding or scaling failed", path)' in images

start = images.index("static void LogImageFailure(")
end = images.index("\nstatic uint8_t* ReadWholeFile(", start)
failure_logger = images[start:end]

fixture = r'''
#include <atomic>
#include <cassert>
#include <cstdarg>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace T7MapImages {
constexpr int kMaxPictures = 128;
constexpr uint32_t kMaxFailureLogs = 64;
constexpr uint64_t kImageRetryDelayUs = 5000000;
struct Picture { char name[96]; uint64_t image; uint32_t attemptEpoch; uint64_t retryAfter; };
static Picture g_pictures[kMaxPictures]{};
static int g_pictureCount = 0;
static std::atomic<uint32_t> g_pictureEpoch{1};
static std::atomic<uint32_t> g_failureLogs{0};
static bool g_fileReady = false;
static bool g_imageValid = true;
static int g_attempts = 0;
static uint64_t g_now = 100000000;
[[maybe_unused]] static uint64_t sceKernelGetProcessTime() { return g_now; }
static std::vector<std::string> g_logs;

static void T7Log_Write(const char* format, ...) {
    char buffer[1024];
    va_list args;
    va_start(args, format);
    std::vsnprintf(buffer, sizeof(buffer), format, args);
    va_end(args);
    g_logs.emplace_back(buffer);
}

FAILURE_LOG

static uint64_t MakePicture(const char*) {
    ++g_attempts;
    return g_fileReady ? static_cast<uint64_t>(1000 + g_attempts) : 0;
}
static bool StillOurs(const Picture&) { return g_imageValid; }

PICTURE_FOR
}
REFRESH

namespace T7Maps {
struct Package { char preview[96]; };
static Package g_packages[1]{};
static uintptr_t g_base = 1;
static int g_readerCount = 0;
static bool g_previewExists = false;
static bool g_loadingExists = false;
static bool g_customExists = false;
static const char* g_thumbnail = nullptr;
struct ListReader { ListReader() { ++g_readerCount; } ~ListReader() { --g_readerCount; } };
static int MapPackage(const char* map) { return std::strcmp(map, "zm_custom") == 0 ? 0 : -1; }
static bool FindPackageFile(const Package& package, const char* file, char* path, size_t size) {
    // The menu scanner calls this outside ListReader; the path API needs the guard.
    if (std::strcmp(file, "previewimage.png") == 0 && !g_previewExists) return false;
    if (std::strcmp(file, "loadingimage.png") == 0 && !g_loadingExists) return false;
    if (std::strcmp(file, "custom.png") == 0 && !g_customExists) return false;
    if (std::strcmp(file, "previewimage.png") != 0 && std::strcmp(file, "loadingimage.png") != 0 &&
        std::strcmp(file, "custom.png") != 0) return false;
    if (g_readerCount > 0 && std::strcmp(package.preview, file) != 0) return false;
    return std::snprintf(path, size, "/data/usermaps/zm_custom/%s", file) < (int)size;
}
static bool JsonString(const char*, const char* key, char* out, size_t size) {
    if (!g_thumbnail || std::strcmp(key, "Thumbnail") != 0) return false;
    return std::snprintf(out, size, "%s", g_thumbnail) < (int)size;
}
static bool EqualsNoCase(const char* left, const char* right) {
    while (*left && *right) {
        const char a = *left >= 'A' && *left <= 'Z' ? *left - 'A' + 'a' : *left;
        const char b = *right >= 'A' && *right <= 'Z' ? *right - 'A' + 'a' : *right;
        if (a != b) return false;
        ++left;
        ++right;
    }
    return *left == *right;
}

FIND_PREVIEW
}

PREVIEW_PATH

int main() {
    using namespace T7MapImages;
    using namespace T7Maps;
    LogImageFailure("bo3customs_preview_zm_custom", "PNG rejected", "/data/custom/preview.png");
    assert(g_logs.size() == 1);
    assert(g_logs[0].find("PNG rejected") != std::string::npos);
    assert(g_logs[0].find("/data/custom/preview.png") != std::string::npos);
    for (int i = 0; i < 80; ++i) LogImageFailure("bo3customs_preview_missing", "path missing", nullptr);
    assert(g_logs.size() == 65); // 64 actual failures + one suppression notice
    assert(g_logs.back().find("suppressed after 64") != std::string::npos);

    const char* icon = "bo3customs_preview_zm_custom";
    assert(PictureFor(icon) == 0);
    assert(PictureFor(icon) == 0 && g_attempts == 1); // missing images are not retried every draw

    g_fileReady = true;
    T7MapImages_RefreshMisses();
    const uint64_t loaded = PictureFor(icon);
    assert(loaded && g_attempts == 2);
    assert(PictureFor(icon) == loaded && g_attempts == 2);

    T7MapImages_RefreshMisses();
    assert(PictureFor(icon) == loaded && g_attempts == 2); // keep live GPU images
    g_imageValid = false;
    assert(PictureFor(icon) != loaded && g_attempts == 3); // stale slot is recreated
    g_imageValid = true;

    const char* missing = "bo3customs_loading_zm_missing";
    g_fileReady = false;
    assert(PictureFor(missing) == 0);
    assert(PictureFor(missing) == 0 && g_attempts == 4);
    T7MapImages_RefreshMisses();
    g_fileReady = true;
    assert(PictureFor(missing) != 0 && g_attempts == 5);

    const char* delayed = "bo3customs_preview_zm_delayed";
    g_fileReady = false;
    assert(PictureFor(delayed) == 0 && g_attempts == 6);
    g_now += 4000000;
    assert(PictureFor(delayed) == 0 && g_attempts == 6); // don't hammer missing files every UI render
    g_now += 1000000;
    assert(PictureFor(delayed) == 0 && g_attempts == 7); // retry without explicit rescan
    g_fileReady = true;
    g_now += 5000000;
    assert(PictureFor(delayed) != 0 && g_attempts == 8); // recover once file becomes available

    g_loadingExists = true;
    FindPreview(g_packages[0], nullptr);
    assert(std::strcmp(g_packages[0].preview, "loadingimage.png") == 0); // missing selection icon has real fallback

    g_customExists = true;
    g_thumbnail = "images/custom.png";
    FindPreview(g_packages[0], "{manifest}");
    assert(std::strcmp(g_packages[0].preview, "custom.png") == 0); // workshop icon wins over loading art

    g_previewExists = true;
    FindPreview(g_packages[0], "{manifest}");
    assert(std::strcmp(g_packages[0].preview, "previewimage.png") == 0); // standard icon takes priority

    g_previewExists = false;
    g_customExists = false;
    g_loadingExists = false;
    FindPreview(g_packages[0], "{manifest}");
    assert(!g_packages[0].preview[0]); // none exists: no broken custom registration

    std::snprintf(g_packages[0].preview, sizeof(g_packages[0].preview), "loadingimage.png");
    g_loadingExists = true;
    char path[128]{};
    assert(T7Maps_MapPreviewPath("zm_custom", path, sizeof(path)));
    assert(std::strcmp(path, "/data/usermaps/zm_custom/loadingimage.png") == 0);
    assert(g_readerCount == 0);
    assert(!T7Maps_MapPreviewPath("missing", path, sizeof(path)));
    assert(!T7Maps_MapPreviewPath("zm_custom", path, 8));
    g_packages[0].preview[0] = 0;
    assert(!T7Maps_MapPreviewPath("zm_custom", path, sizeof(path)));
    std::puts("PASS: loading-image fallback, thumbnail priority, timed retry, refresh retry, live GPU cache, bounded diagnostics");
}
'''.replace("PICTURE_FOR", picture_for).replace("REFRESH", refresh).replace("PREVIEW_PATH", preview_path).replace("FIND_PREVIEW", find_preview).replace("FAILURE_LOG", failure_logger)

with tempfile.TemporaryDirectory(prefix="bo3-map-icons-") as directory:
    cpp = Path(directory) / "icons.cpp"
    binary = Path(directory) / "icons"
    cpp.write_text(fixture, encoding="utf-8")
    subprocess.run([os.environ.get("CXX", "g++"), "-std=c++20", "-Wall", "-Wextra", "-Werror",
                    "-fsanitize=address,undefined", str(cpp), "-o", str(binary)], check=True)
    subprocess.run([str(binary)], check=True)
