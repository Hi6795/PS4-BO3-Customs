#pragma once

#include <stdint.h>
#include <stddef.h>

void T7Lua_Install(uintptr_t base);

void T7Lua_Tick();

bool T7Lua_RunOnTop(uintptr_t L, const char* source, const char* chunk);

bool T7Lua_MouseReady();

bool T7Lua_DvarString(const char* name, char* out, size_t size);

bool T7Lua_IsUiState(uintptr_t L);

void T7Lua_RunShareVm(uintptr_t L);


// Read-only, try-lock diagnostic snapshot; it does not execute Lua.
void T7Lua_TraceState(const char* reason);
