#include "mixr_prefs.hpp"

#include <stdint.h>

static uint8_t s_brightness = 100U;

static uint32_t s_focus_preset_sec = 25U * 60U;
static uint32_t s_focus_remaining_sec = 0;
static bool s_focus_running = false;

uint8_t mixr_brightness_get(void)
{
    return s_brightness;
}

void mixr_brightness_set(uint8_t v)
{
    if (v < MIXR_BRIGHTNESS_MIN) {
        v = MIXR_BRIGHTNESS_MIN;
    }
    if (v > MIXR_BRIGHTNESS_MAX) {
        v = MIXR_BRIGHTNESS_MAX;
    }
    s_brightness = v;
}

uint32_t mixr_focus_preset_sec_get(void)
{
    return s_focus_preset_sec;
}

void mixr_focus_preset_add_sec(int32_t delta_sec)
{
    int64_t v = (int64_t)s_focus_preset_sec + (int64_t)delta_sec;
    const int64_t lo = 60;
    const int64_t hi = 7200;
    if (v < lo) {
        v = lo;
    }
    if (v > hi) {
        v = hi;
    }
    s_focus_preset_sec = (uint32_t)v;
}

bool mixr_focus_is_running(void)
{
    return s_focus_running;
}

uint32_t mixr_focus_remaining_sec_get(void)
{
    return s_focus_remaining_sec;
}

void mixr_focus_start(void)
{
    s_focus_remaining_sec = s_focus_preset_sec;
    s_focus_running = true;
}

void mixr_focus_stop(void)
{
    s_focus_running = false;
    s_focus_remaining_sec = 0;
}

bool mixr_focus_tick_1s(void)
{
    if (!s_focus_running || s_focus_remaining_sec == 0) {
        return false;
    }
    if (s_focus_remaining_sec > 0) {
        s_focus_remaining_sec--;
    }
    if (s_focus_remaining_sec == 0) {
        s_focus_running = false;
    }
    return true;
}
