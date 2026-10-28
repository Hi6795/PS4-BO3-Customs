#pragma once

#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include "t7_maps.hpp"
#include "t7_log.hpp"

// Read-only companion files. Configuration/saves/log writes keep their existing paths.
namespace T7LuaFiles
{
inline bool SafePath(const char* path, bool absolute)
{
    if (!path || !*path || (path[0] == '/') != absolute)
        return false;
    const char* segment = path;
    for (const char* at = path;; ++at)
    {
        const unsigned char c = (unsigned char)*at;
        if (c && (c < 32 || c == 127 || c == '\\' || c == ':'))
            return false;
        if (!c || c == '/')
        {
            const size_t length = (size_t)(at - segment);
            if ((length == 1 && segment[0] == '.') ||
                (length == 2 && segment[0] == '.' && segment[1] == '.'))
                return false;
            segment = at + 1;
        }
        if (!c)
            return true;
    }
}

inline char* ReadAt(const char* path, size_t maximum, size_t* length, bool text)
{
    const int fd = sceKernelOpen(path, SCE_KERNEL_O_RDONLY, 0);
    if (fd < 0)
        return nullptr;
    const int64_t size = sceKernelLseek(fd, 0, SEEK_END);
    char* body = nullptr;
    size_t used = 0;
    if (size > 0 && (uint64_t)size <= maximum && sceKernelLseek(fd, 0, SEEK_SET) == 0)
        body = (char*)malloc((size_t)size + 1);
    while (body && used < (size_t)size)
    {
        const int64_t got = sceKernelRead(fd, body + used, (size_t)size - used);
        if (got <= 0 || (uint64_t)got > (size_t)size - used)
            break;
        used += (size_t)got;
    }
    char extra;
    const bool complete = body && used == (size_t)size && sceKernelRead(fd, &extra, 1) == 0;
    const bool closed = sceKernelClose(fd) >= 0;
    if (!complete || !closed || (text && memchr(body, 0, used)))
    {
        free(body);
        return nullptr;
    }
    body[used] = 0;
    if (length)
        *length = used;
    return body;
}

inline char* Read(const char* relative, size_t maximum, size_t* length = nullptr, bool text = true)
{
    if (length)
        *length = 0;
    if (!maximum || !SafePath(relative, false))
        return nullptr;
    // Always attempt the internal file first, including after an external hit.
    // Internal hits need one open and never probe or enumerate external drives.
    char path[512];
    const int internal = snprintf(path, sizeof(path), "/data/BO3-Customs/%s", relative);
    if (internal < 0 || (size_t)internal >= sizeof(path))
        return nullptr;
    if (char* body = ReadAt(path, maximum, length, text))
        return body;

    const size_t drives = sizeof(k_driveRoots) / sizeof(k_driveRoots[0]);
    for (size_t i = 0; i < drives; ++i)
    {
        const int count = snprintf(path, sizeof(path), "/%s/BO3-Customs/%s", k_driveRoots[i], relative);
        if (count < 0 || (size_t)count >= sizeof(path))
            continue;
        char* body = ReadAt(path, maximum, length, text);
        if (body)
        {
            T7Log_Write("[Lua] companion %s read from /%s/BO3-Customs", relative, k_driveRoots[i]);
            return body;
        }
    }
    return nullptr;
}
}
