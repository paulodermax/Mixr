#include "ui_mixr.hpp"
#include "menu_catalog.hpp"
#include "mixr_prefs.hpp"
#include "mixr_settings.hpp"
#include "pins_config.h"

#include "esp_system.h"

#include <cstdio>
#include <cstring>

static lv_obj_t *screen_loading;
static lv_obj_t *screen_player;
static lv_obj_t *dim_layer;
static lv_obj_t *ui_title;
static lv_obj_t *ui_artist;
static lv_obj_t *ui_cover;
static lv_obj_t *menu_overlay;
static lv_obj_t *menu_col;
static lv_obj_t *menu_title_lbl;
static lv_obj_t *menu_special;
static lv_obj_t *brightness_panel;
static lv_obj_t *brightness_bar;
static lv_obj_t *brightness_val_lbl;
static lv_obj_t *focus_run_panel;
static lv_obj_t *focus_run_lbl;
static lv_obj_t *debug_corner = nullptr;
static lv_obj_t *debug_lbl = nullptr;
static lv_obj_t *err_banner_lbl = nullptr;
/** Cover-Rahmen für Layout (Titel/Artist darunter) */
static lv_obj_t *s_cover_frame = nullptr;

static char s_reset_tag[14] = "R:?";

/** Vom PC empfangen; Anzeige erst mit Cover (IMAGE_READY) gemeinsam */
static char s_pending_title[64] = "-";
static char s_pending_artist[64] = "-";

static uint8_t *s_cover_buf = nullptr;
static uint32_t s_cover_bytes = 0;
static lv_image_dsc_t s_img_dsc = {};

static int menu_sel = 0;
static size_t menu_count = 0;
/** Focus screen: row 0 selected, encoder adjusts timer only while true (toggle with press on row 0) */
static bool s_focus_timer_edit = false;
static MenuScreenId s_last_rebuilt_screen = MenuScreenId::Root;

static void rebuild_list_rows(void);
static void apply_menu_highlight(void);
static void create_global_dim_layers(void);
static void strip_display_layers(lv_display_t *disp);

static void apply_menu_highlight(void)
{
    if (menu_col == nullptr) {
        return;
    }
    size_t cat_n = 0;
    const MenuItemDef *cat_items = menu_catalog_items(&cat_n);
    uint32_t n = lv_obj_get_child_count(menu_col);
    for (uint32_t i = 0; i < n; i++) {
        lv_obj_t *row = lv_obj_get_child(menu_col, i);
        if (row == nullptr) {
            continue;
        }
        if ((int)i == menu_sel) {
            lv_obj_set_style_bg_opa(row, LV_OPA_COVER, 0);
            lv_obj_set_style_bg_color(row, lv_palette_darken(LV_PALETTE_BLUE, 2), 0);
        } else {
            lv_obj_set_style_bg_opa(row, LV_OPA_TRANSP, 0);
        }
        lv_obj_t *lbl = lv_obj_get_child(row, 0);
        if (lbl) {
            lv_color_t unsel = lv_color_hex(0xd0d0d8);
            if (menu_nav_current() == MenuScreenId::Settings && cat_items != nullptr && (size_t)i < cat_n
                && std::strcmp(cat_items[i].id, "restart") == 0) {
                unsel = lv_palette_main(LV_PALETTE_PINK);
            }
            lv_obj_set_style_text_color(lbl, (int)i == menu_sel ? lv_color_white() : unsel, 0);
        }
    }
}

static void menu_row_clicked(lv_event_t *e)
{
    intptr_t idx = reinterpret_cast<intptr_t>(lv_event_get_user_data(e));
    const MenuItemDef *items = menu_catalog_items(&menu_count);
    if (items == nullptr || (size_t)idx >= menu_count) {
        return;
    }
    menu_sel = (int)idx;
    apply_menu_highlight();
    mixr_ui_menu_navigate(0, true);
}

static void rebuild_list_rows(void)
{
    if (menu_col == nullptr) {
        return;
    }
    lv_obj_clean(menu_col);
    const MenuItemDef *items = menu_catalog_items(&menu_count);
    if (items == nullptr || menu_count == 0) {
        return;
    }
    for (size_t i = 0; i < menu_count; i++) {
        lv_obj_t *row = lv_obj_create(menu_col);
        lv_obj_set_width(row, LV_PCT(100));
        lv_obj_set_height(row, LV_SIZE_CONTENT);
        lv_obj_set_style_radius(row, 0, 0);
        lv_obj_set_style_pad_ver(row, 14, 0);
        lv_obj_set_style_pad_hor(row, 4, 0);
        lv_obj_set_style_border_width(row, 0, 0);
        lv_obj_set_style_border_opa(row, LV_OPA_TRANSP, 0);
        lv_obj_set_style_outline_width(row, 0, 0);
        lv_obj_set_style_outline_opa(row, LV_OPA_TRANSP, 0);
        lv_obj_set_style_shadow_width(row, 0, 0);
        lv_obj_add_flag(row, LV_OBJ_FLAG_CLICKABLE);
        lv_obj_t *lbl = lv_label_create(row);
        lv_label_set_text(lbl, menu_catalog_row_title(i));
        lv_obj_set_width(lbl, LV_PCT(100));
        lv_obj_set_style_text_font(lbl, &lv_font_montserrat_14, 0);
        if (menu_nav_current() == MenuScreenId::Settings && std::strcmp(items[i].id, "restart") == 0) {
            lv_obj_set_style_text_color(lbl, lv_palette_main(LV_PALETTE_PINK), 0);
        }
        lv_obj_set_style_text_align(lbl, LV_TEXT_ALIGN_CENTER, 0);
        lv_obj_align(lbl, LV_ALIGN_CENTER, 0, 0);
        lv_obj_add_event_cb(row, menu_row_clicked, LV_EVENT_CLICKED, reinterpret_cast<void *>(static_cast<intptr_t>(i)));
    }
}

static void set_menu_title_for_screen(MenuScreenId s)
{
    if (menu_title_lbl == nullptr) {
        return;
    }
    switch (s) {
        case MenuScreenId::Root:
            lv_label_set_text(menu_title_lbl, "Menu");
            break;
        case MenuScreenId::Settings:
            lv_label_set_text(menu_title_lbl, "Settings");
            break;
        case MenuScreenId::Brightness:
            lv_label_set_text(menu_title_lbl, "Brightness");
            break;
        case MenuScreenId::Restart:
            lv_label_set_text(menu_title_lbl, "Restart");
            break;
        case MenuScreenId::Debug:
            lv_label_set_text(menu_title_lbl, "Debug");
            break;
        case MenuScreenId::Playback:
            lv_label_set_text(menu_title_lbl, "Playback Control");
            break;
        case MenuScreenId::Focus:
            lv_label_set_text(menu_title_lbl, "Focus mode (app)");
            break;
    }
}

void mixr_ui_apply_prefs_to_display(void)
{
    if (dim_layer == nullptr) {
        return;
    }
    uint8_t b = mixr_brightness_get();
    int opa = 255 - (int)((unsigned)b * 255U / 100U);
    if (opa < 0) {
        opa = 0;
    }
    if (opa > 255) {
        opa = 255;
    }
    lv_obj_set_style_bg_opa(dim_layer, (lv_opa_t)opa, 0);
}

void mixr_ui_focus_refresh(void)
{
    if (focus_run_lbl == nullptr) {
        return;
    }
    uint32_t r = mixr_focus_remaining_sec_get();
    uint32_t m = r / 60U;
    uint32_t s = r % 60U;
    char buf[24];
    snprintf(buf, sizeof(buf), "%02lu:%02lu", (unsigned long)m, (unsigned long)s);
    lv_label_set_text(focus_run_lbl, buf);
}

void mixr_ui_on_focus_timer_tick(void)
{
    if (!mixr_focus_tick_1s()) {
        return;
    }
    if (mixr_focus_is_running()) {
        mixr_ui_focus_refresh();
    } else {
        s_focus_timer_edit = false;
        if (mixr_ui_is_menu_open() && menu_nav_current() == MenuScreenId::Focus) {
            mixr_ui_menu_rebuild();
        }
    }
}

void mixr_ui_menu_rebuild(void)
{
    if (menu_col == nullptr || menu_special == nullptr) {
        return;
    }

    MenuScreenId s = menu_nav_current();
    if (s == MenuScreenId::Focus && !mixr_focus_is_running() && s_last_rebuilt_screen != MenuScreenId::Focus) {
        s_focus_timer_edit = false;
    }
    s_last_rebuilt_screen = s;

    set_menu_title_for_screen(s);

    if (s == MenuScreenId::Brightness) {
        lv_obj_add_flag(menu_col, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(menu_special, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(brightness_panel, LV_OBJ_FLAG_HIDDEN);
        lv_obj_add_flag(focus_run_panel, LV_OBJ_FLAG_HIDDEN);
        uint8_t b = mixr_brightness_get();
        lv_bar_set_value(brightness_bar, b, LV_ANIM_OFF);
        char t[16];
        snprintf(t, sizeof(t), "%u%%", (unsigned)b);
        lv_label_set_text(brightness_val_lbl, t);
        mixr_ui_apply_prefs_to_display();
    } else if (s == MenuScreenId::Focus && mixr_focus_is_running()) {
        lv_obj_add_flag(menu_col, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(menu_special, LV_OBJ_FLAG_HIDDEN);
        lv_obj_add_flag(brightness_panel, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(focus_run_panel, LV_OBJ_FLAG_HIDDEN);
        mixr_ui_focus_refresh();
    } else {
        lv_obj_clear_flag(menu_col, LV_OBJ_FLAG_HIDDEN);
        lv_obj_add_flag(menu_special, LV_OBJ_FLAG_HIDDEN);
        rebuild_list_rows();
    }

    menu_sel = 0;
    apply_menu_highlight();
}

static void build_menu_overlay(lv_obj_t *parent_screen)
{
    (void)parent_screen;
    menu_overlay = lv_obj_create(lv_layer_top());
    lv_obj_set_size(menu_overlay, LV_PCT(100), LV_PCT(100));
    lv_obj_set_style_bg_color(menu_overlay, lv_color_hex(0x0e0e12), 0);
    lv_obj_set_style_bg_opa(menu_overlay, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(menu_overlay, 0, 0);
    lv_obj_set_style_border_opa(menu_overlay, LV_OPA_TRANSP, 0);
    lv_obj_set_style_outline_width(menu_overlay, 0, 0);
    lv_obj_set_style_outline_opa(menu_overlay, LV_OPA_TRANSP, 0);
    lv_obj_set_style_shadow_width(menu_overlay, 0, 0);
    lv_obj_set_style_pad_all(menu_overlay, 0, 0);

    /* Volle Breite, kein zweites „Karten“-Rechteck (vorher 92 % + Radius = sichtbarer Rahmen) */
    lv_obj_t *panel = lv_obj_create(menu_overlay);
    lv_obj_set_width(panel, LV_PCT(100));
    lv_obj_set_height(panel, LV_SIZE_CONTENT);
    lv_obj_align(panel, LV_ALIGN_CENTER, 0, 0);
    lv_obj_set_style_bg_opa(panel, LV_OPA_TRANSP, 0);
    lv_obj_set_style_radius(panel, 0, 0);
    lv_obj_set_style_pad_all(panel, 16, 0);
    lv_obj_set_style_border_width(panel, 0, 0);
    lv_obj_set_style_border_opa(panel, LV_OPA_TRANSP, 0);
    lv_obj_set_style_outline_width(panel, 0, 0);
    lv_obj_set_style_outline_opa(panel, LV_OPA_TRANSP, 0);
    lv_obj_set_style_shadow_width(panel, 0, 0);
    lv_obj_clear_flag(panel, LV_OBJ_FLAG_SCROLLABLE);

    menu_title_lbl = lv_label_create(panel);
    lv_label_set_text(menu_title_lbl, "Menu");
    lv_obj_set_width(menu_title_lbl, LV_PCT(100));
    lv_obj_set_style_text_font(menu_title_lbl, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_align(menu_title_lbl, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_style_text_color(menu_title_lbl, lv_color_white(), 0);
    lv_obj_align(menu_title_lbl, LV_ALIGN_TOP_MID, 0, 8);

    menu_col = lv_obj_create(panel);
    lv_obj_set_width(menu_col, LV_PCT(100));
    lv_obj_set_height(menu_col, LV_SIZE_CONTENT);
    lv_obj_align_to(menu_col, menu_title_lbl, LV_ALIGN_OUT_BOTTOM_MID, 0, 16);
    lv_obj_set_style_bg_opa(menu_col, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(menu_col, 0, 0);
    lv_obj_set_style_pad_all(menu_col, 0, 0);
    lv_obj_set_flex_flow(menu_col, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(menu_col, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_START);
    lv_obj_set_style_pad_row(menu_col, 6, 0);
    lv_obj_clear_flag(menu_col, LV_OBJ_FLAG_SCROLLABLE);

    menu_special = lv_obj_create(panel);
    lv_obj_set_width(menu_special, LV_PCT(100));
    lv_obj_set_height(menu_special, LV_SIZE_CONTENT);
    lv_obj_align_to(menu_special, menu_title_lbl, LV_ALIGN_OUT_BOTTOM_MID, 0, 16);
    lv_obj_set_style_bg_opa(menu_special, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(menu_special, 0, 0);
    lv_obj_set_style_pad_all(menu_special, 0, 0);
    lv_obj_set_flex_flow(menu_special, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_style_pad_row(menu_special, 10, 0);
    lv_obj_clear_flag(menu_special, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_add_flag(menu_special, LV_OBJ_FLAG_HIDDEN);

    brightness_panel = lv_obj_create(menu_special);
    lv_obj_set_width(brightness_panel, LV_PCT(100));
    lv_obj_set_height(brightness_panel, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_opa(brightness_panel, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(brightness_panel, 0, 0);
    lv_obj_set_style_pad_all(brightness_panel, 0, 0);
    lv_obj_set_flex_flow(brightness_panel, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_style_pad_row(brightness_panel, 8, 0);

    brightness_bar = lv_bar_create(brightness_panel);
    lv_obj_set_width(brightness_bar, LV_PCT(100));
    lv_obj_set_height(brightness_bar, 14);
    lv_bar_set_range(brightness_bar, (int32_t)MIXR_BRIGHTNESS_MIN, (int32_t)MIXR_BRIGHTNESS_MAX);
    lv_bar_set_value(brightness_bar, (int32_t)mixr_brightness_get(), LV_ANIM_OFF);
    lv_obj_set_style_border_width(brightness_bar, 0, LV_PART_MAIN);
    lv_obj_set_style_border_opa(brightness_bar, LV_OPA_TRANSP, LV_PART_MAIN);
    lv_obj_set_style_outline_width(brightness_bar, 0, LV_PART_MAIN);
    lv_obj_set_style_border_width(brightness_bar, 0, LV_PART_INDICATOR);
    lv_obj_set_style_border_opa(brightness_bar, LV_OPA_TRANSP, LV_PART_INDICATOR);
    lv_obj_set_style_outline_width(brightness_bar, 0, LV_PART_INDICATOR);

    brightness_val_lbl = lv_label_create(brightness_panel);
    lv_label_set_text(brightness_val_lbl, "100%");
    lv_obj_set_style_text_color(brightness_val_lbl, lv_color_hex(0xe8e8ee), 0);
    lv_obj_set_width(brightness_val_lbl, LV_PCT(100));
    lv_obj_set_style_text_align(brightness_val_lbl, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_style_text_font(brightness_val_lbl, &lv_font_montserrat_14, 0);

    focus_run_panel = lv_obj_create(menu_special);
    lv_obj_set_width(focus_run_panel, LV_PCT(100));
    lv_obj_set_height(focus_run_panel, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_opa(focus_run_panel, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(focus_run_panel, 0, 0);
    lv_obj_set_style_pad_ver(focus_run_panel, 16, 0);

    focus_run_lbl = lv_label_create(focus_run_panel);
    lv_label_set_text(focus_run_lbl, "00:00");
    lv_obj_set_style_text_font(focus_run_lbl, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(focus_run_lbl, lv_color_hex(0xf2f2f8), 0);
    lv_obj_center(focus_run_lbl);

    lv_obj_add_flag(focus_run_panel, LV_OBJ_FLAG_HIDDEN);

    (void)menu_catalog_items(&menu_count);
    rebuild_list_rows();
    menu_sel = 0;
    apply_menu_highlight();
    lv_obj_add_flag(menu_overlay, LV_OBJ_FLAG_HIDDEN);
}

static void create_debug_corner(uint32_t boot_count)
{
    debug_corner = lv_obj_create(lv_layer_top());
    lv_obj_set_width(debug_corner, LV_SIZE_CONTENT);
    lv_obj_set_height(debug_corner, LV_SIZE_CONTENT);
    lv_obj_align(debug_corner, LV_ALIGN_BOTTOM_RIGHT, -3, -3);
    lv_obj_set_style_bg_color(debug_corner, lv_color_hex(0x0a0a10), 0);
    lv_obj_set_style_bg_opa(debug_corner, LV_OPA_80, 0);
    lv_obj_set_style_radius(debug_corner, 6, 0);
    lv_obj_set_style_pad_hor(debug_corner, 6, 0);
    lv_obj_set_style_pad_ver(debug_corner, 4, 0);
    lv_obj_set_style_border_width(debug_corner, 0, 0);
    lv_obj_set_style_border_opa(debug_corner, LV_OPA_TRANSP, 0);
    lv_obj_set_style_outline_width(debug_corner, 0, 0);
    lv_obj_set_style_outline_opa(debug_corner, LV_OPA_TRANSP, 0);
    lv_obj_clear_flag(debug_corner, LV_OBJ_FLAG_SCROLLABLE);

    debug_lbl = lv_label_create(debug_corner);
    lv_obj_set_style_text_font(debug_lbl, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(debug_lbl, lv_color_hex(0xa8b4d0), 0);
    lv_label_set_text(debug_lbl, "--");
    lv_obj_center(debug_lbl);

    mixr_ui_set_debug_overlay(boot_count, 0);
}

static void strip_display_layers(lv_display_t *disp)
{
    if (disp == nullptr) {
        return;
    }
    lv_obj_t *layers[] = {
        lv_display_get_layer_top(disp),
        lv_display_get_layer_sys(disp),
        lv_display_get_layer_bottom(disp),
    };
    for (lv_obj_t *o : layers) {
        if (o == nullptr) {
            continue;
        }
        lv_obj_set_style_border_width(o, 0, 0);
        lv_obj_set_style_border_opa(o, LV_OPA_TRANSP, 0);
        lv_obj_set_style_outline_width(o, 0, 0);
        lv_obj_set_style_outline_opa(o, LV_OPA_TRANSP, 0);
        lv_obj_set_style_shadow_width(o, 0, 0);
        lv_obj_set_style_pad_all(o, 0, 0);
        lv_obj_set_style_radius(o, 0, 0);
    }
}

/** Full-screen dim on top layer (above menu & debug) so all UI is affected; input passes through */
static void create_global_dim_layers(void)
{
    dim_layer = lv_obj_create(lv_layer_top());
    lv_obj_set_size(dim_layer, TFT_WIDTH, TFT_HEIGHT);
    lv_obj_align(dim_layer, LV_ALIGN_TOP_LEFT, 0, 0);
    lv_obj_set_style_bg_color(dim_layer, lv_color_black(), 0);
    lv_obj_set_style_bg_opa(dim_layer, LV_OPA_TRANSP, 0);
    lv_obj_clear_flag(dim_layer, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_clear_flag(dim_layer, LV_OBJ_FLAG_CLICKABLE);
}

void mixr_ui_init(lv_display_t *disp, uint8_t *cover_buf, uint32_t cover_bytes, uint32_t boot_count)
{
    s_cover_buf = cover_buf;
    s_cover_bytes = cover_bytes;

    screen_loading = lv_obj_create(nullptr);
    lv_obj_set_size(screen_loading, TFT_WIDTH, TFT_HEIGHT);
    lv_obj_set_style_bg_color(screen_loading, lv_color_hex(0x050508), 0);
    lv_obj_set_style_pad_all(screen_loading, 0, 0);
    lv_obj_set_style_border_width(screen_loading, 0, 0);
    lv_obj_set_style_outline_width(screen_loading, 0, 0);

    lv_obj_t *lbl = lv_label_create(screen_loading);
    lv_label_set_text(lbl, "Connect USB...");
    lv_obj_set_style_text_color(lbl, lv_color_hex(0xe8e8ee), 0);
    lv_obj_set_style_text_font(lbl, &lv_font_montserrat_14, 0);
    lv_obj_align(lbl, LV_ALIGN_CENTER, 0, 0);

    screen_player = lv_obj_create(nullptr);
    lv_obj_set_size(screen_player, TFT_WIDTH, TFT_HEIGHT);
    lv_obj_set_style_bg_color(screen_player, lv_color_hex(0x0e0e12), 0);
    lv_obj_set_style_pad_all(screen_player, 0, 0);
    lv_obj_set_style_border_width(screen_player, 0, 0);
    lv_obj_set_style_outline_width(screen_player, 0, 0);

    s_cover_frame = lv_obj_create(screen_player);
    lv_obj_t *cover_frame = s_cover_frame;
    lv_obj_set_size(cover_frame, 240, 240);
    /* Etwas nach unten, damit Text darunter mehr Platz hat */
    lv_obj_align(cover_frame, LV_ALIGN_TOP_MID, 0, 36);
    lv_obj_set_style_radius(cover_frame, 14, 0);
    lv_obj_set_style_clip_corner(cover_frame, true, 0);
    lv_obj_set_style_border_width(cover_frame, 0, 0);
    lv_obj_set_style_shadow_width(cover_frame, 18, 0);
    lv_obj_set_style_shadow_ofs_y(cover_frame, 6, 0);
    lv_obj_set_style_shadow_opa(cover_frame, LV_OPA_40, 0);
    lv_obj_set_style_bg_opa(cover_frame, LV_OPA_TRANSP, 0);
    lv_obj_clear_flag(cover_frame, LV_OBJ_FLAG_SCROLLABLE);

    ui_cover = lv_image_create(cover_frame);
    lv_obj_set_size(ui_cover, 240, 240);
    lv_obj_center(ui_cover);

    ui_title = lv_label_create(screen_player);
    lv_label_set_text(ui_title, "-");
#if defined(LV_FONT_MONTSERRAT_18) && LV_FONT_MONTSERRAT_18
    lv_obj_set_style_text_font(ui_title, &lv_font_montserrat_18, 0);
#else
    lv_obj_set_style_text_font(ui_title, &lv_font_montserrat_14, 0);
#endif
    lv_obj_set_style_text_color(ui_title, lv_color_hex(0xf2f2f8), 0);
    lv_label_set_long_mode(ui_title, LV_LABEL_LONG_DOT);
    lv_obj_set_width(ui_title, 228);
    lv_obj_set_style_text_align(ui_title, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_align_to(ui_title, cover_frame, LV_ALIGN_OUT_BOTTOM_MID, 0, 10);

    ui_artist = lv_label_create(screen_player);
    lv_label_set_text(ui_artist, "-");
#if defined(LV_FONT_MONTSERRAT_18) && LV_FONT_MONTSERRAT_18
    lv_obj_set_style_text_font(ui_artist, &lv_font_montserrat_18, 0);
#else
    lv_obj_set_style_text_font(ui_artist, &lv_font_montserrat_14, 0);
#endif
    lv_obj_set_style_text_color(ui_artist, lv_palette_lighten(LV_PALETTE_GREY, 1), 0);
    lv_label_set_long_mode(ui_artist, LV_LABEL_LONG_DOT);
    lv_obj_set_width(ui_artist, 228);
    lv_obj_set_style_text_align(ui_artist, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_align_to(ui_artist, ui_title, LV_ALIGN_OUT_BOTTOM_MID, 0, 6);

    err_banner_lbl = lv_label_create(screen_player);
    lv_label_set_text(err_banner_lbl, "");
    lv_obj_set_style_text_font(err_banner_lbl, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(err_banner_lbl, lv_palette_main(LV_PALETTE_RED), 0);
    lv_label_set_long_mode(err_banner_lbl, LV_LABEL_LONG_DOT);
    lv_obj_set_width(err_banner_lbl, 228);
    lv_obj_align(err_banner_lbl, LV_ALIGN_BOTTOM_MID, 0, -44);
    lv_obj_add_flag(err_banner_lbl, LV_OBJ_FLAG_HIDDEN);

    lv_obj_t *hint = lv_label_create(screen_player);
    lv_label_set_text(hint, "Encoder: Menu");
    lv_obj_set_style_text_font(hint, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(hint, lv_palette_darken(LV_PALETTE_GREY, 1), 0);
    lv_obj_align(hint, LV_ALIGN_BOTTOM_MID, 0, -14);

    build_menu_overlay(screen_player);
    create_debug_corner(boot_count);
    create_global_dim_layers();
    mixr_ui_apply_prefs_to_display();
    strip_display_layers(disp);
}

void mixr_ui_set_last_reset_reason(int reset_reason_as_int)
{
    esp_reset_reason_t r = static_cast<esp_reset_reason_t>(reset_reason_as_int);
    const char *t = "?";
    switch (r) {
        case ESP_RST_POWERON:
            t = "POR";
            break;
        case ESP_RST_USB:
            t = "USB";
            break;
        case ESP_RST_SW:
            t = "SW";
            break;
        case ESP_RST_PANIC:
            t = "PAN";
            break;
        case ESP_RST_INT_WDT:
            t = "IWD";
            break;
        case ESP_RST_TASK_WDT:
            t = "TWD";
            break;
        case ESP_RST_WDT:
            t = "WD";
            break;
        case ESP_RST_BROWNOUT:
            t = "BRN";
            break;
        case ESP_RST_DEEPSLEEP:
            t = "SLP";
            break;
        default:
            t = "???";
            break;
    }
    snprintf(s_reset_tag, sizeof(s_reset_tag), "R:%s", t);
}

void mixr_ui_set_error_banner(const char *text_utf8)
{
    if (err_banner_lbl == nullptr) {
        return;
    }
    if (text_utf8 == nullptr || text_utf8[0] == '\0') {
        lv_obj_add_flag(err_banner_lbl, LV_OBJ_FLAG_HIDDEN);
        lv_label_set_text(err_banner_lbl, "");
        return;
    }
    lv_label_set_text(err_banner_lbl, text_utf8);
    lv_obj_clear_flag(err_banner_lbl, LV_OBJ_FLAG_HIDDEN);
}

void mixr_ui_set_debug_overlay(uint32_t boot_count, uint32_t uptime_sec)
{
    if (debug_lbl == nullptr) {
        return;
    }
    char buf[64];
    snprintf(buf, sizeof(buf), "#%lu %s\n%lus\nS%u B%u",
             (unsigned long)boot_count, s_reset_tag, (unsigned long)uptime_sec,
             mixr_sliders_send_enabled() ? 1U : 0U, mixr_buttons_send_enabled() ? 1U : 0U);
    lv_label_set_text(debug_lbl, buf);
}

void mixr_ui_set_usb_connected(bool connected)
{
    menu_catalog_set_usb_connected(connected);
    lv_screen_load(connected ? screen_player : screen_loading);
    if (mixr_ui_is_menu_open() && (menu_nav_current() == MenuScreenId::Debug)) {
        mixr_ui_menu_refresh_dynamic_rows();
    }
}

void mixr_ui_on_message(const UiMessage *msg)
{
    if (msg == nullptr) {
        return;
    }
    switch (msg->type) {
        case PktType::SONG_TITLE:
            std::strncpy(s_pending_title, msg->payload.text, sizeof(s_pending_title) - 1);
            s_pending_title[sizeof(s_pending_title) - 1] = '\0';
            break;
        case PktType::SONG_ARTIST:
            std::strncpy(s_pending_artist, msg->payload.text, sizeof(s_pending_artist) - 1);
            s_pending_artist[sizeof(s_pending_artist) - 1] = '\0';
            break;
        case PktType::IMAGE_READY:
            if (ui_cover && s_cover_buf && s_cover_bytes >= 240U * 240U * 2U) {
                std::memset(&s_img_dsc, 0, sizeof(s_img_dsc));
                s_img_dsc.header.magic = LV_IMAGE_HEADER_MAGIC;
                s_img_dsc.header.cf = LV_COLOR_FORMAT_RGB565;
                s_img_dsc.header.w = 240;
                s_img_dsc.header.h = 240;
                s_img_dsc.header.stride = 240 * 2;
                s_img_dsc.data_size = 240 * 240 * 2;
                s_img_dsc.data = s_cover_buf;
                lv_image_set_src(ui_cover, &s_img_dsc);
                if (ui_title) {
                    lv_label_set_text(ui_title, s_pending_title);
                }
                if (ui_artist) {
                    lv_label_set_text(ui_artist, s_pending_artist);
                }
                mixr_ui_set_error_banner("");
            } else {
                mixr_ui_set_error_banner("Cover: kein PSRAM");
            }
            break;
        default:
            break;
    }
}

void mixr_ui_menu_refresh_dynamic_rows(void)
{
    if (menu_col == nullptr) {
        return;
    }
    uint32_t n = lv_obj_get_child_count(menu_col);
    for (uint32_t i = 0; i < n; i++) {
        lv_obj_t *row = lv_obj_get_child(menu_col, i);
        if (row == nullptr) {
            continue;
        }
        lv_obj_t *lbl = lv_obj_get_child(row, 0);
        if (lbl) {
            lv_label_set_text(lbl, menu_catalog_row_title(i));
        }
    }
    apply_menu_highlight();
}

void mixr_ui_menu_refresh_settings_rows(void)
{
    mixr_ui_menu_refresh_dynamic_rows();
}

void mixr_ui_enter_menu(void)
{
    if (menu_overlay == nullptr) {
        return;
    }
    menu_nav_reset();
    lv_obj_clear_flag(menu_overlay, LV_OBJ_FLAG_HIDDEN);
    menu_sel = 0;
    mixr_ui_menu_rebuild();
}

void mixr_ui_enter_song_view_from_menu(void)
{
    if (menu_overlay == nullptr) {
        return;
    }
    lv_obj_add_flag(menu_overlay, LV_OBJ_FLAG_HIDDEN);
}

bool mixr_ui_is_menu_open(void)
{
    if (menu_overlay == nullptr) {
        return false;
    }
    return !lv_obj_has_flag(menu_overlay, LV_OBJ_FLAG_HIDDEN);
}

void mixr_ui_menu_navigate(int8_t detent_delta, bool activate_click)
{
    MenuScreenId screen = menu_nav_current();

    if (screen == MenuScreenId::Brightness) {
        if (activate_click) {
            menu_nav_pop();
            mixr_ui_menu_rebuild();
            return;
        }
        if (detent_delta != 0) {
            int v = (int)mixr_brightness_get() + (int)detent_delta;
            if (v < (int)MIXR_BRIGHTNESS_MIN) {
                v = (int)MIXR_BRIGHTNESS_MIN;
            }
            if (v > (int)MIXR_BRIGHTNESS_MAX) {
                v = (int)MIXR_BRIGHTNESS_MAX;
            }
            mixr_brightness_set((uint8_t)v);
            lv_bar_set_value(brightness_bar, v, LV_ANIM_OFF);
            char t[16];
            snprintf(t, sizeof(t), "%d%%", v);
            lv_label_set_text(brightness_val_lbl, t);
            mixr_ui_apply_prefs_to_display();
        }
        return;
    }

    if (screen == MenuScreenId::Focus && mixr_focus_is_running()) {
        if (activate_click) {
            s_focus_timer_edit = false;
            mixr_focus_stop();
            mixr_ui_menu_rebuild();
        }
        return;
    }

    if (screen == MenuScreenId::Focus && !mixr_focus_is_running()) {
        if (activate_click) {
            if (menu_sel == 0) {
                menu_nav_pop();
                mixr_ui_menu_rebuild();
            } else if (menu_sel == 1) {
                s_focus_timer_edit = !s_focus_timer_edit;
                mixr_ui_menu_refresh_dynamic_rows();
                apply_menu_highlight();
            } else if (menu_sel == 2) {
                mixr_focus_start();
                s_focus_timer_edit = false;
                mixr_ui_menu_rebuild();
            }
            return;
        }
        if (detent_delta != 0) {
            const MenuItemDef *items = menu_catalog_items(&menu_count);
            if (items == nullptr || menu_count < 3) {
                return;
            }
            if (menu_sel == 1 && s_focus_timer_edit) {
                if (detent_delta > 0) {
                    mixr_focus_preset_add_sec(60);
                } else if (mixr_focus_preset_sec_get() > 60U) {
                    mixr_focus_preset_add_sec(-60);
                }
                mixr_ui_menu_refresh_dynamic_rows();
            } else {
                const int n = (int)menu_count;
                int a = menu_sel + (int)detent_delta;
                menu_sel = (a % n + n) % n;
                if (menu_sel != 1) {
                    s_focus_timer_edit = false;
                }
                apply_menu_highlight();
            }
        }
        return;
    }

    const MenuItemDef *items = menu_catalog_items(&menu_count);
    if (items == nullptr || menu_count == 0) {
        return;
    }

    if (activate_click) {
        if (items[menu_sel].on_activate) {
            items[menu_sel].on_activate();
        }
        return;
    }

    if (detent_delta == 0) {
        return;
    }

    const int n = (int)menu_count;
    int a = menu_sel + (int)detent_delta;
    menu_sel = (a % n + n) % n;
    apply_menu_highlight();
}
