#pragma once
// Target ABI declarations; see ../README.md for provenance.
#include <fcntl.h>
#include <sys/stat.h>
#include <pthread.h>
#include <stddef.h>
#define NotificationRequest bo3_sdk_notification_declaration
#define sceKernelSendNotificationRequest bo3_sdk_send_notification_declaration
#define scePthreadSetaffinity bo3_sdk_affinity_declaration
#define sceKernelDebugOutText bo3_sdk_debugout_declaration
#define sceKernelAvailableFlexibleMemorySize bo3_sdk_flexible_memory_declaration
#include <orbis/libkernel.h>
#undef sceKernelAvailableFlexibleMemorySize
#undef sceKernelDebugOutText
#undef NotificationRequest
#undef sceKernelSendNotificationRequest
#undef scePthreadSetaffinity
#include <orbis/_types/user.h>
extern "C" int scePthreadSetaffinity(OrbisPthread, uint64_t);
// The v0.5.4 public header spells this output pointer as size_t by mistake.
extern "C" int32_t sceKernelAvailableFlexibleMemorySize(size_t*);
using ScePthread = OrbisPthread;
using ScePthreadAttr = OrbisPthreadAttr;
#include "kernel_stat.hpp"
extern "C" int32_t bo3_sceKernelStat(const char*, SceKernelStat*) __asm__("sceKernelStat");
extern "C" int32_t bo3_sceKernelFstat(int32_t, SceKernelStat*) __asm__("sceKernelFstat");
#define sceKernelStat bo3_sceKernelStat
#define sceKernelFstat bo3_sceKernelFstat
struct SceKernelDirent {
    uint32_t d_fileno;
    uint16_t d_reclen;
    uint8_t d_type, d_namlen;
    char d_name[256];
};
struct SceKernelVirtualQueryInfo {
    void* start;
    void* end;
    off_t offset;
    int32_t protection, memoryType;
    unsigned isFlexibleMemory:1, isDirectMemory:1, isStack:1, isPooledMemory:1, isCommitted:1;
    char name[32];
};
static_assert(sizeof(SceKernelVirtualQueryInfo) == sizeof(OrbisKernelVirtualQueryInfo));
static_assert(offsetof(SceKernelVirtualQueryInfo, name) == offsetof(OrbisKernelVirtualQueryInfo, name));
extern "C" int32_t bo3_sceKernelVirtualQuery(const void*, int32_t, SceKernelVirtualQueryInfo*, size_t) __asm__("sceKernelVirtualQuery");
#define sceKernelVirtualQuery bo3_sceKernelVirtualQuery
#define SCE_KERNEL_VQ_FIND_NEXT 1
#define SCE_KERNEL_O_RDONLY 0x0000
#define SCE_KERNEL_O_WRONLY 0x0001
#define SCE_KERNEL_O_RDWR 0x0002
#define SCE_KERNEL_O_APPEND 0x0008
#define SCE_KERNEL_O_CREAT 0x0200
#define SCE_KERNEL_O_TRUNC 0x0400
#define SCE_KERNEL_O_DIRECTORY 0x00020000
#define SCE_KERNEL_DT_DIR 4
#define SCE_KERNEL_S_ISDIR S_ISDIR
#define SCE_KERNEL_PRIO_FIFO_HIGHEST ORBIS_KERNEL_PRIO_FIFO_HIGHEST
#define T7Unity_InstallExceptionHandler __PS4_NID_WkwEd3N7w0Y
#define T7Unity_RaiseException __PS4_NID_il03nluKfMk
