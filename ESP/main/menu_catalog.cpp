#include "menu_catalog.hpp"
#include "mixr_prefs.hpp"
#include "mixr_settings.hpp"
#include "ui_mixr.hpp"
#include "esp_log.h"

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

static void cb_open_debug(void)
{
    menu_nav_push(MenuScreenId::Debug);
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

static const MenuItemDef g_root[] = {
    {"focus", "Focus mode (app)", cb_open_focus},
    {"settings", "Settings", cb_open_settings},
    {"close", "Back", cb_close_to_player},
};

static const MenuItemDef g_settings[] = {
    {"brightness", "Brightness", cb_open_brightness},
    {"debug", "Debug", cb_open_debug},
    {"back", "Back", cb_back_pop},
};

static const MenuItemDef g_debug[] = {
    {"sliders_tx", "", cb_toggle_sliders_tx},
    {"buttons_tx", "", cb_toggle_buttons_tx},
    {"usb", "", cb_noop},
    {"touch", "", cb_toggle_touch},
    {"back", "Back", cb_back_pop},
};

static const MenuItemDef g_focus[] = {
    {"focus_time", "", cb_noop},
    {"focus_start", "Start", cb_noop},
    {"back", "Back", cb_back_pop},
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
        return s_usb_connected ? "USB-C: connected" : "USB-C: disconnected";
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
        case MenuScreenId::Debug:
            *out_count = sizeof(g_debug) / sizeof(g_debug[0]);
            return g_debug;
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
