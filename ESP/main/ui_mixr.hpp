#pragma once

#include "lvgl.h"
#include "protocol.h"
#include <cstdint>

void mixr_ui_init(lv_display_t *disp, uint8_t *cover_buf, uint32_t cover_bytes, uint32_t boot_count);

void mixr_ui_set_debug_overlay(uint32_t boot_count, uint32_t uptime_sec);

void mixr_ui_set_usb_connected(bool connected);

void mixr_ui_on_message(const UiMessage *msg);

void mixr_ui_enter_menu(void);

void mixr_ui_enter_song_view_from_menu(void);

bool mixr_ui_is_menu_open(void);

void mixr_ui_menu_navigate(int8_t detent_delta, bool activate_click);

void mixr_ui_menu_rebuild(void);

void mixr_ui_menu_refresh_dynamic_rows(void);

/** @deprecated alias */
void mixr_ui_menu_refresh_settings_rows(void);

void mixr_ui_apply_prefs_to_display(void);

void mixr_ui_focus_refresh(void);

/** 1 Hz aus Hauptschleife: Countdown + UI */
void mixr_ui_on_focus_timer_tick(void);
