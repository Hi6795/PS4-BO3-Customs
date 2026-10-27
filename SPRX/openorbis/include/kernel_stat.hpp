#pragma once

#include <stddef.h>
#include <stdint.h>

// PS4 kernel ABI, independent of the SDK libc's mode_t/include order.
// https://github.com/shadps4-emu/shadPS4/blob/main/src/core/libraries/kernel/file_system.h
struct Bo3KernelStatTimespec {
    int64_t tv_sec;
    int64_t tv_nsec;
};

struct SceKernelStat {
    uint32_t st_dev;
    uint32_t st_ino;
    uint16_t st_mode;
    uint16_t st_nlink;
    uint32_t st_uid;
    uint32_t st_gid;
    uint32_t st_rdev;
    Bo3KernelStatTimespec st_atim;
    Bo3KernelStatTimespec st_mtim;
    Bo3KernelStatTimespec st_ctim;
    int64_t st_size;
    int64_t st_blocks;
    uint32_t st_blksize;
    uint32_t st_flags;
    uint32_t st_gen;
    int32_t st_lspare;
    Bo3KernelStatTimespec st_birthtim;
};

static_assert(sizeof(Bo3KernelStatTimespec) == 16);
static_assert(sizeof(SceKernelStat) == 120 && alignof(SceKernelStat) == 8);
static_assert(offsetof(SceKernelStat, st_mode) == 8);
static_assert(offsetof(SceKernelStat, st_nlink) == 10);
static_assert(offsetof(SceKernelStat, st_uid) == 12);
static_assert(offsetof(SceKernelStat, st_gid) == 16);
static_assert(offsetof(SceKernelStat, st_rdev) == 20);
static_assert(offsetof(SceKernelStat, st_atim) == 24);
static_assert(offsetof(SceKernelStat, st_mtim) == 40);
static_assert(offsetof(SceKernelStat, st_ctim) == 56);
static_assert(offsetof(SceKernelStat, st_size) == 72);
static_assert(offsetof(SceKernelStat, st_blocks) == 80);
static_assert(offsetof(SceKernelStat, st_blksize) == 88);
static_assert(offsetof(SceKernelStat, st_flags) == 92);
static_assert(offsetof(SceKernelStat, st_gen) == 96);
static_assert(offsetof(SceKernelStat, st_lspare) == 100);
static_assert(offsetof(SceKernelStat, st_birthtim) == 104);
