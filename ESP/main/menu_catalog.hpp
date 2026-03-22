#pragma once

#include <stddef.h>
#include <stdint.h>

enum class MenuScreenId : uint8_t {
    Root,
    Settings,
    Brightness,
    Restart,
    Debug,
    Playback,
    Focus,
};

struct MenuItemDef {
    const char *id;
    const char *title;
    void (*on_activate)(void);
};

void menu_nav_reset(void);
void menu_nav_push(MenuScreenId s);
void menu_nav_pop(void);
MenuScreenId menu_nav_current(void);

void menu_catalog_set_usb_connected(bool connected);

const MenuItemDef *menu_catalog_items(size_t *out_count);

const char *menu_catalog_row_title(size_t index);
