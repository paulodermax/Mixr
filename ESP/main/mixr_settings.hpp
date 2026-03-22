#pragma once

#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

bool mixr_sliders_send_enabled(void);
bool mixr_buttons_send_enabled(void);
bool mixr_touch_enabled(void);

void mixr_sliders_send_set(bool on);
void mixr_buttons_send_set(bool on);
void mixr_touch_set(bool on);

void mixr_sliders_send_toggle(void);
void mixr_buttons_send_toggle(void);
void mixr_touch_toggle(void);

/** Aktuelle Slider in die Baseline übernehmen und optional einmal an den PC senden (nach „Slider: ON“). */
void mixr_sliders_resync_baseline(void);

#ifdef __cplusplus
}
#endif
