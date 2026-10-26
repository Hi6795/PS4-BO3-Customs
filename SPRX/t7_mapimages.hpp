#pragma once

#include <stdint.h>

#define T7_USERMAP_PREVIEW_PREFIX "bo3customs_preview_"
#define T7_USERMAP_LOADING_PREFIX "bo3customs_loading_"
#define T7_MOD_PREVIEW_PREFIX "bo3customs_modpreview_"

void T7MapImages_Install(uintptr_t base);

// Allow unavailable map artwork to be retried after the map registry rescans.
void T7MapImages_RefreshMisses();
