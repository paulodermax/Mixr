#include "mixr_settings.hpp"

static bool s_sliders_send = true;
static bool s_buttons_send = true;
static bool s_touch = true;

extern "C" {

bool mixr_touch_enabled(void)
{
    return s_touch;
}

void mixr_touch_set(bool on)
{
    s_touch = on;
}

void mixr_touch_toggle(void)
{
    s_touch = !s_touch;
}

bool mixr_sliders_send_enabled(void)
{
    return s_sliders_send;
}

bool mixr_buttons_send_enabled(void)
{
    return s_buttons_send;
}

void mixr_sliders_send_set(bool on)
{
    s_sliders_send = on;
}

void mixr_buttons_send_set(bool on)
{
    s_buttons_send = on;
}

void mixr_sliders_send_toggle(void)
{
    s_sliders_send = !s_sliders_send;
}

void mixr_buttons_send_toggle(void)
{
    s_buttons_send = !s_buttons_send;
}

} // extern "C"
