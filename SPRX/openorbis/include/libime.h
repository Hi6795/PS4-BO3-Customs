#pragma once
#include <stdint.h>
#include <stddef.h>
struct SceImeKeycode {
    uint16_t keycode;
    char16_t character;
    uint32_t status, type;
    int32_t userId;
    uint32_t resourceId;
    uint64_t timestamp;
};
union SceImeEventParam { SceImeKeycode keycode; int8_t reserved[64]; };
struct SceImeEvent { uint32_t id; SceImeEventParam param; };
static_assert(sizeof(SceImeKeycode) == 32 && offsetof(SceImeKeycode, status) == 4);
static_assert(sizeof(SceImeEvent) == 72 && offsetof(SceImeEvent, param) == 8);
#define SCE_IME_KEYCODE_STATE_KEYCODE_VALID 1
#define SCE_IME_KEYBOARD_EVENT_KEYCODE_DOWN 257
#define SCE_IME_KEYBOARD_EVENT_KEYCODE_UP 258
#define SCE_IME_KEYBOARD_EVENT_KEYCODE_REPEAT 259
#define SCE_IME_KEYBOARD_EVENT_DISCONNECTION 261
#define SCE_IME_KEYBOARD_EVENT_ABORT 262
