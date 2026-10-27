#pragma once
#include <stdint.h>
#include <stddef.h>
struct SceMouseOpenParam { uint8_t behaviorFlag; uint8_t reserved[7]; };
struct SceMouseData {
    uint64_t timestamp;
    bool connected;
    uint32_t buttons;
    int32_t xAxis, yAxis, wheel, tilt;
    uint8_t reserved[8];
};
static_assert(sizeof(SceMouseOpenParam) == 8);
static_assert(sizeof(SceMouseData) == 40 && offsetof(SceMouseData, buttons) == 12 && offsetof(SceMouseData, xAxis) == 16);
extern "C" {
    int sceMouseInit();
    int sceMouseOpen(int32_t, int32_t, int32_t, SceMouseOpenParam*);
    int sceMouseClose(int);
    int sceMouseRead(int, SceMouseData*, int);
}
#define SCE_MOUSE_BUTTON_INTERCEPTED 0x80000000u
#define SCE_MOUSE_OPEN_PARAM_MERGED 1
#define SCE_MOUSE_PORT_TYPE_STANDARD 0
