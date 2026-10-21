#pragma once

#include <stdint.h>

void T7Kbm_Install(uintptr_t base);

bool T7Kbm_LanguagePrefix(char* out, size_t size);

void T7Kbm_SetPauseMenu(bool open);
