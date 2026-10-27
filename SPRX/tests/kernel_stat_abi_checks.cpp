// Host regression: compile with png.cpp; optional arguments are real PNG files.
#include "../openorbis/include/kernel_stat.hpp"
#include "../png.hpp"

#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

// Reproduce the pinned SDK record when its first mode_t definition wins.
struct LegacySdkStat {
    uint32_t dev, ino, mode;
    uint16_t nlink;
    uint32_t uid, gid, rdev;
    Bo3KernelStatTimespec atim, mtim, ctim;
    int64_t size, blocks;
    uint32_t blksize, flags, gen;
    Bo3KernelStatTimespec birthtim;
};
static_assert(sizeof(LegacySdkStat) == 128);
static_assert(offsetof(LegacySdkStat, size) == 80);

template <typename T>
static void Store(uint8_t* bytes, size_t offset, T value)
{
    memcpy(bytes + offset, &value, sizeof(value));
}

static SceKernelStat KernelRecord(int64_t bytes, int64_t blocks, LegacySdkStat* old = nullptr)
{
    // Kernel output is serialized independently of either C++ record layout.
    uint8_t native[120] = {};
    Store<uint16_t>(native, 8, 0100644);
    Store<uint16_t>(native, 10, 1);
    Store<uint32_t>(native, 12, 1000);
    Store<int64_t>(native, 72, bytes);
    Store<int64_t>(native, 80, blocks);
    Store<uint32_t>(native, 88, 16384);
    SceKernelStat fixed{};
    memcpy(&fixed, native, sizeof(native));
    if (old) {
        memset(old, 0, sizeof(*old));
        memcpy(old, native, sizeof(native));
    }
    assert(fixed.st_size == bytes && fixed.st_blocks == blocks);
    assert(fixed.st_mode == 0100644 && fixed.st_nlink == 1 && fixed.st_uid == 1000);
    assert(fixed.st_blksize == 16384);
    return fixed;
}

int main(int argc, char** argv)
{
    LegacySdkStat old{};
    KernelRecord(343564, 672, &old);
    assert(old.size == 672); // The old reader used blocks as a byte count.
    KernelRecord(0, 0);
    KernelRecord((1ll << 33) + 7, 512);

    for (int i = 1; i < argc; ++i) {
        FILE* file = fopen(argv[i], "rb");
        assert(file);
        assert(fseek(file, 0, SEEK_END) == 0);
        const long size = ftell(file);
        assert(size > 0 && fseek(file, 0, SEEK_SET) == 0);
        uint8_t* data = (uint8_t*)malloc((size_t)size);
        assert(data && fread(data, 1, (size_t)size, file) == (size_t)size);
        fclose(file);

        const SceKernelStat fixed = KernelRecord(size, (size + 511) / 512, &old);
        PngImage image{};
        const char* why = nullptr;
        assert(!Png_Open(data, (size_t)old.size, &image, &why));
        printf("old: %ld bytes read as %lld: %s\n", size, (long long)old.size, why);
        why = nullptr;
        assert(Png_Open(data, (size_t)fixed.st_size, &image, &why));
        free(data);
    }
    printf("PS4 stat/fstat ABI fixture passed; %d PNG files accepted with corrected byte counts.\n", argc - 1);
}
