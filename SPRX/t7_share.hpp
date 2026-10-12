#pragma once

#include <stdint.h>
#include <stddef.h>

constexpr size_t kT7ShareName = 56;
constexpr size_t kT7SharePath = 224;
constexpr int    kT7ShareMods = 6;

struct T7ShareStatus
{
    char  title[64];
    char  name[128];
    char  detail[160];
    char  file[192];
    float fraction;
    bool  failed;
};

void T7Share_Install(uintptr_t base);

void T7Share_Tick();

float T7Share_Query(const char* what);

bool T7Share_Wants(const char* map);

void T7Share_LobbyState(uintptr_t L);

bool T7Share_Status(T7ShareStatus* status);

void T7Share_Cancel();

bool T7Share_TakeMapsChanged();
