#pragma once

#include "lvgl.h"
#include "protocol.h"
#include <cstdint>

void mixr_ui_init(lv_display_t *disp, uint8_t *cover_buf, uint32_t cover_bytes, uint32_t boot_count);

void mixr_ui_set_debug_overlay(uint32_t boot_count, uint32_t uptime_sec);

bool mixr_ui_debug_overlay_is_visible(void);
void mixr_ui_debug_overlay_toggle(void);

/** Letzter Chip-Reset (Kurzform), für Debug-Ecke */
void mixr_ui_set_last_reset_reason(int reset_reason_as_int);

/** Fehlertext unten auf dem Player-Screen; leer = ausblenden */
void mixr_ui_set_error_banner(const char *text_utf8);

void mixr_ui_set_usb_connected(bool connected);

void mixr_ui_on_message(const UiMessage *msg);

/** Anzeige-Zustand Slide-0-Overlays (für Debug-Menü-Zeilen). */
bool mixr_ui_voip_mic_muted_displayed(void);
bool mixr_ui_voip_deafened_displayed(void);

/** Legacy: zeigt nur noch „Close“ (Navigation laeuft ueber Slides). */
void mixr_ui_enter_menu(void);

/**
 * Wenn kein Menü offen: zuerst Slides drehen; ein Klick aktiviert die Folie.
 * Danach Encoder = Optionen (Focus/Settings-Raster), erneuter Klick = Aktion; oben rechts „<<“ beendet die Aktivierung
 * (nicht auf der Cover-Folie). Carousel wechselt die Folie nur im nicht-aktivierten Modus bzw. auf Cover/Platzhalter.
 */
void mixr_ui_main_navigate(int8_t detent_delta, bool activate_click);

void mixr_ui_enter_song_view_from_menu(void);

bool mixr_ui_is_menu_open(void);

void mixr_ui_menu_navigate(int8_t detent_delta, bool activate_click);

void mixr_ui_menu_rebuild(void);

void mixr_ui_menu_refresh_dynamic_rows(void);

void mixr_ui_brightness_enter_list_mode(void);

/** Langdruck Encoder: erste Folie (Cover), Menü zu */
void mixr_ui_goto_first_slide(void);

void mixr_ui_apply_prefs_to_display(void);

/** 1 Hz aus Hauptschleife: Countdown + UI */
void mixr_ui_on_focus_timer_tick(void);
