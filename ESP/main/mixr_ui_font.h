#pragma once

#include "lvgl.h"

LV_FONT_DECLARE(lv_font_montserrat_22);
LV_FONT_DECLARE(lv_font_montserrat_40);

/** Einheitliche UI-Schrift: LVGL Montserrat 22 (built-in). */
#define MIXR_UI_FONT (&lv_font_montserrat_22)

/** Focus-Slide: große Minuten, kleinere Sekunden */
#define MIXR_UI_FONT_CLOCK_MIN (&lv_font_montserrat_40)
#define MIXR_UI_FONT_CLOCK_SEC (&lv_font_montserrat_22)
