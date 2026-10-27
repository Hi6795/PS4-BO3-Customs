#pragma once

#include "t7_share.hpp"

inline const char* const k_driveRoots[] =
{
    "ext0", "ext1", "ext2", "ext3", "ext4", "ext5", "ext6", "ext7",
    "usb0", "usb1", "usb2", "usb3", "usb4", "usb5", "usb6", "usb7",
};

void T7Maps_Install(uintptr_t base);

int T7Maps_MapCount();

bool T7Maps_Refresh();

bool T7Maps_Uninstall(const char* map, char* message, size_t size);

int T7Maps_UninstallResult(char* map, size_t mapSize, char* message, size_t messageSize);

bool T7Maps_InLevel();

const char* T7Maps_CustomMapsLua();

bool T7Maps_MapFile(const char* map, const char* file, char* path, size_t size);

const char* T7Maps_PreviewFile(const char* map);

bool T7Maps_ModPreviewFile(uint32_t key, char* path, size_t size);

void T7Maps_FrameHeartbeat();

// Return the owning zero-based loaded-mod slot, -1 if untracked, or -2 if the claim table is busy.
int T7Maps_ScriptOwner(const char* script);

void T7Maps_TraceDiagnostics(const char* reason);

void T7Maps_FrameWork();

void T7Maps_RequestApply();

void T7Maps_RequestPrefetch();

void T7Maps_MenusBuilt();

bool T7Maps_MenusBroken();

bool T7Maps_DllModLoaded();

void T7Maps_FirstModUgc(char* out, size_t size);

bool T7Maps_ModSave(char* out, size_t size);

int T7Maps_SelectMod(const char* name, char* message, size_t size);

const char* T7Maps_ModsLua();

const char* T7Maps_ModFilesLua();

bool T7Maps_ModsChanged();

int T7Maps_ModCount();

bool T7Maps_UsingMods();

bool T7Maps_HasMap(const char* name);

int T7Maps_MapNames(char (*names)[kT7ShareName], int capacity);

bool T7Maps_EngineHasMap(const char* name);

bool T7Maps_HasMod(const char* name);

void T7Maps_SetSessionMods(const char (*names)[kT7ShareName], int count);

bool T7Maps_ShareMap(const char* map, char* root, size_t size);

int T7Maps_ShareMods(char (*names)[kT7ShareName], char (*roots)[kT7SharePath], int capacity);

void T7Maps_ShareRoot(bool mods, char* out, size_t size);
