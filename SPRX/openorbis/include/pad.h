#pragma once
#include <orbis/Pad.h>
#include <stddef.h>
struct ScePadTouch { uint16_t x, y; uint8_t id, reserved[3]; };
struct ScePadTouchData { uint8_t touchNum, reserved[3]; uint32_t reserved2; ScePadTouch touch[2]; };
struct ScePadData {
    uint32_t buttons;
    stick leftStick, rightStick;
    analog analogButtons;
    uint16_t padding;
    vec_float4 orientation;
    vec_float3 acceleration, angularVelocity;
    ScePadTouchData touchData;
    uint8_t connected;
    uint64_t timestamp;
    uint8_t ext[16], count, unknown[15];
};
static_assert(sizeof(ScePadData) == sizeof(OrbisPadData));
static_assert(sizeof(ScePadTouch) == sizeof(OrbisPadTouch));
static_assert(offsetof(ScePadData, touchData) == offsetof(OrbisPadData, touch));
static_assert(offsetof(ScePadData, connected) == offsetof(OrbisPadData, connected));
extern "C" int32_t bo3_scePadReadState(int32_t, ScePadData*) __asm__("scePadReadState");
#define scePadReadState bo3_scePadReadState
#define SCE_PAD_BUTTON_TOUCH_PAD ORBIS_PAD_BUTTON_TOUCH_PAD
#define SCE_PAD_PORT_TYPE_STANDARD ORBIS_PAD_PORT_TYPE_STANDARD
