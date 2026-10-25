#pragma once

#include <stddef.h>
#include <stdint.h>

// Only the platform boundary is substituted; the detour and decoder are real.
int sceKernelMmap(void*, size_t, int, int, int, int64_t, void**);
int sceKernelMunmap(void*, size_t);
int sceKernelMprotect(const void*, size_t, int);
int sceKernelUsleep(unsigned int);
