#pragma once

typedef int (*sceKernelDebugOutText_t)(int32_t dbg_channel, const char* text);
inline sceKernelDebugOutText_t sceKernelDebugOutText = nullptr;

uint64_t GetBaseAddress();

bool RangeReadable(uintptr_t addr, size_t span);

// Scans no further than maxBytes or the end of verified readable memory.
// A NUL within that range is required; length is only written on success.
bool SafeReadableCString(uintptr_t addr, size_t maxBytes, size_t* length);

bool SafeStrStr(uintptr_t addr, const char* target, size_t maxScan = 64);

void Notify(const char* fmt, ...);

bool IsPs5();

const char* DebugSink();

void DebugOut(const char* text);
