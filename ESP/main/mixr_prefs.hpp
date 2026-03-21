#pragma once

#include <stdbool.h>
#include <stdint.h>

#define MIXR_DEVICE_NAME "Mixr"

#define MIXR_BRIGHTNESS_MIN 5U
#define MIXR_BRIGHTNESS_MAX 100U

uint8_t mixr_brightness_get(void);
void mixr_brightness_set(uint8_t v);

uint32_t mixr_focus_preset_sec_get(void);
void mixr_focus_preset_add_sec(int32_t delta_sec);

bool mixr_focus_is_running(void);
uint32_t mixr_focus_remaining_sec_get(void);
void mixr_focus_start(void);
void mixr_focus_stop(void);

/** 1 Hz; true wenn Anzeige aktualisiert werden soll */
bool mixr_focus_tick_1s(void);
