#include "menu_catalog.hpp"
#include "mixr_app.hpp"
#include "mixr_prefs.hpp"
#include "mixr_settings.hpp"
#include "protocol.h"
#include "ui_mixr.hpp"
#include "esp_log.h"
#include "esp_system.h"

#include <cstdio>
#include <cstring>

static const char *TAG = "menu_cat";

static bool s_usb_connected = false;

static MenuScreenId s_stack[8] = {MenuScreenId::Root};
static int s_sp = 0;

void menu_nav_reset(void)
{
    s_sp = 0;
    s_stack[0] = MenuScreenId::Root;
}

void menu_nav_push(MenuScreenId s)
{
    if (s_sp < 7) {
        s_stack[++s_sp] = s;
    }
}

void menu_nav_pop(void)
{
    if (s_sp > 0) {
        s_sp--;
    }
}

MenuScreenId menu_nav_current(void)
{
    return s_stack[s_sp];
}

void menu_catalog_set_usb_connected(bool connected)
{
    s_usb_connected = connected;
}

/* --- callbacks --- */

static void cb_close_to_player(void)
{
    menu_nav_reset();
    mixr_ui_enter_song_view_from_menu();
}

static void cb_open_settings(void)
{
    menu_nav_push(MenuScreenId::Settings);
    mixr_ui_menu_rebuild();
}

static void cb_open_focus(void)
{
    menu_nav_push(MenuScreenId::Focus);
    mixr_ui_menu_rebuild();
}

static void cb_open_brightness(void)
{
    menu_nav_push(MenuScreenId::Brightness);
    mixr_ui_menu_rebuild();
}

static void cb_open_restart(void)
{
    menu_nav_push(MenuScreenId::Restart);
    mixr_ui_menu_rebuild();
}

static void cb_restart_yes(void)
{
    esp_restart();
}

static void cb_restart_no(void)
{
    menu_nav_pop();
    mixr_ui_menu_rebuild();
}

static void cb_open_debug(void)
{
    menu_nav_push(MenuScreenId::Debug);
    mixr_ui_menu_rebuild();
}

static void cb_open_playback(void)
{
    menu_nav_push(MenuScreenId::Playback);
    mixr_ui_menu_rebuild();
}

static void cb_back_pop(void)
{
    menu_nav_pop();
    mixr_ui_menu_rebuild();
}

static void cb_toggle_touch(void)
{
    mixr_touch_toggle();
    ESP_LOGI(TAG, "Touch: %s", mixr_touch_enabled() ? "ON" : "OFF");
    mixr_ui_menu_refresh_dynamic_rows();
}

static void cb_toggle_sliders_tx(void)
{
    mixr_sliders_send_toggle();
    ESP_LOGI(TAG, "Slider USB: %s", mixr_sliders_send_enabled() ? "ON" : "OFF");
    mixr_ui_menu_refresh_dynamic_rows();
}

static void cb_toggle_buttons_tx(void)
{
    mixr_buttons_send_toggle();
    ESP_LOGI(TAG, "Button USB: %s", mixr_buttons_send_enabled() ? "ON" : "OFF");
    mixr_ui_menu_refresh_dynamic_rows();
}

static void cb_noop(void) {}

static void cb_media_next(void)
{
    mixr_pc_send_media_cmd(static_cast<uint8_t>(MediaSubCmd::NEXT));
    ESP_LOGI(TAG, "-> PC: Next");
}

static void cb_media_play_pause(void)
{
    mixr_pc_send_media_cmd(static_cast<uint8_t>(MediaSubCmd::PLAY_PAUSE));
    ESP_LOGI(TAG, "-> PC: Play/Pause");
}

static void cb_media_previous(void)
{
    mixr_pc_send_media_cmd(static_cast<uint8_t>(MediaSubCmd::PREVIOUS));
    ESP_LOGI(TAG, "-> PC: Previous");
}

static const MenuItemDef g_root[] = {
    {"close", "Back", cb_close_to_player},
    {"focus", "Focus mode (app)", cb_open_focus},
    {"settings", "Settings", cb_open_settings},
};

static const MenuItemDef g_settings[] = {
    {"back", "Back", cb_back_pop},
    {"brightness", "Brightness", cb_open_brightness},
    {"restart", "Restart", cb_open_restart},
    {"debug", "Debug", cb_open_debug},
};

static const MenuItemDef g_restart[] = {
    {"back", "Back", cb_restart_no},
    {"restart_sure", "Are you sure?", cb_noop},
    {"restart_yes", "Yes", cb_restart_yes},
    {"restart_no", "No", cb_restart_no},
};

static const MenuItemDef g_debug[] = {
    {"back", "Back", cb_back_pop},
    {"sliders_tx", "", cb_toggle_sliders_tx},
    {"buttons_tx", "", cb_toggle_buttons_tx},
    {"touch", "", cb_toggle_touch},
    {"usb", "", cb_noop},
    {"playback", "Playback Control ->", cb_open_playback},
};

static const MenuItemDef g_playback[] = {
    {"back", "Back", cb_back_pop},
    {"next", "Next Song", cb_media_next},
    {"play_pause", "Pause/Play", cb_media_play_pause},
    {"prev", "Last Song", cb_media_previous},
};

static const MenuItemDef g_focus[] = {
    {"back", "Back", cb_back_pop},
    {"focus_time", "", cb_noop},
    {"focus_start", "Start", cb_noop},
};

const char *menu_catalog_row_title(size_t index)
{
    size_t n = 0;
    const MenuItemDef *items = menu_catalog_items(&n);
    if (items == nullptr || index >= n) {
        return "";
    }
    if (std::strcmp(items[index].id, "touch") == 0) {
        return mixr_touch_enabled() ? "Touch: ON" : "Touch: OFF";
    }
    if (std::strcmp(items[index].id, "sliders_tx") == 0) {
        return mixr_sliders_send_enabled() ? "Slider: ON" : "Slider: OFF";
    }
    if (std::strcmp(items[index].id, "buttons_tx") == 0) {
        return mixr_buttons_send_enabled() ? "Button: ON" : "Button: OFF";
    }
    if (std::strcmp(items[index].id, "usb") == 0) {
        return s_usb_connected ? "USB-C connected: Yes" : "USB-C connected: No";
    }
    if (std::strcmp(items[index].id, "focus_time") == 0) {
        uint32_t t = mixr_focus_preset_sec_get();
        uint32_t m = t / 60U;
        uint32_t s = t % 60U;
        static char buf[28];
        snprintf(buf, sizeof(buf), "Timer: %02lu:%02lu", (unsigned long)m, (unsigned long)s);
        return buf;
    }
    return items[index].title;
}

const MenuItemDef *menu_catalog_items(size_t *out_count)
{
    switch (menu_nav_current()) {
        case MenuScreenId::Root:
            *out_count = sizeof(g_root) / sizeof(g_root[0]);
            return g_root;
        case MenuScreenId::Settings:
            *out_count = sizeof(g_settings) / sizeof(g_settings[0]);
            return g_settings;
        case MenuScreenId::Brightness:
            *out_count = 0;
            return nullptr;
        case MenuScreenId::Restart:
            *out_count = sizeof(g_restart) / sizeof(g_restart[0]);
            return g_restart;
        case MenuScreenId::Debug:
            *out_count = sizeof(g_debug) / sizeof(g_debug[0]);
            return g_debug;
        case MenuScreenId::Playback:
            *out_count = sizeof(g_playback) / sizeof(g_playback[0]);
            return g_playback;
        case MenuScreenId::Focus:
            if (mixr_focus_is_running()) {
                *out_count = 0;
                return nullptr;
            }
            *out_count = sizeof(g_focus) / sizeof(g_focus[0]);
            return g_focus;
    }
    *out_count = 0;
    return nullptr;
}
