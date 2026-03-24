#include "ui_mixr.hpp"
#include "ui_assets.h"
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
static lv_obj_t *menu_panel = nullptr;
static lv_obj_t *focus_run_root = nullptr;
static lv_obj_t *focus_run_lbl = nullptr;
static lv_obj_t *debug_corner = nullptr;
static lv_obj_t *debug_lbl = nullptr;
static lv_obj_t *err_banner_lbl = nullptr;
/** Cover-Rahmen für Layout (Titel/Artist darunter) */
static lv_obj_t *s_cover_frame = nullptr;

#define MIXR_SLIDE_COUNT 4
#define MIXR_PAGER_H 28
#define MIXR_CAROUSEL_H (TFT_HEIGHT - MIXR_PAGER_H)
/** Figma: Hintergrund / Vordergrund (Text, Boxen, UI) */
#define MIXR_COLOR_BG 0x765858
#define MIXR_COLOR_FG 0xD9D9D9
/** Obere Zeile „Back“ auf Slide 2/3 (nur im Slide-Bearbeitungsmodus) */
#define MIXR_SLIDE_TOPBAR_H 40

static lv_obj_t *s_carousel = nullptr;
static lv_obj_t *s_slide_pages[MIXR_SLIDE_COUNT];
static lv_obj_t *s_pager_dots[MIXR_SLIDE_COUNT];
static lv_obj_t *s_pager_row = nullptr;
static uint8_t s_slide_idx = 0;
static lv_obj_t *s_sl1_preset_lbl = nullptr;
static lv_obj_t *s_mute_mic_img = nullptr;
static lv_obj_t *s_mute_head_img = nullptr;
static bool s_mute_mic_on = false;
static bool s_mute_full_on = false;

/** Carousel: erst nach Encoder-Klick „aktiv“; dann Optionen bedienen (außer Cover-Folie). */
static bool s_slide_edit_active = false;
static uint8_t s_sl1_focus_idx = 0;
static lv_obj_t *s_sl1_tri_up[3] = {nullptr};
static lv_obj_t *s_sl1_tri_down[3] = {nullptr};
static lv_obj_t *s_sl1_start_lbl = nullptr;
static lv_obj_t *s_slide_back_btn = nullptr;
static lv_obj_t *s_sl2_back_row = nullptr;
static lv_obj_t *s_sl2_ph = nullptr;
static lv_obj_t *s_sl3_top = nullptr;
static lv_obj_t *s_sl3_grid = nullptr;
static lv_obj_t *s_grid_cells[6] = {nullptr};
static uint8_t s_grid_nav_pos = 0;
static const uint8_t k_settings_grid_nav[] = {0, 1, 4, 5};
static const uint8_t k_settings_grid_nav_n = 4;

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
static void apply_solid_screen_bg(lv_obj_t *scr);
static void player_intro_opa_cb(void *var, int32_t v);
static void mixr_ui_player_start_intro(void);
static void build_main_carousel(lv_obj_t *parent);
static void mixr_ui_goto_slide(uint8_t idx);
static void mixr_ui_update_pager_dots(void);
static void carousel_scroll_cb(lv_event_t *e);
static void mixr_ui_sync_slide_focus_preset(void);
static void mixr_ui_focus_start_from_slide(void);
static void settings_grid_btn_cb(lv_event_t *e);
static void mixr_ui_open_settings_from_grid_cell(intptr_t cell_idx);
static void mixr_ui_carousel_set_scroll_locked(bool locked);
static void mixr_ui_update_carousel_interaction(void);
static void mixr_ui_update_slide_back_btn(void);
static void sl1_refresh_focus_visuals(void);
static void settings_grid_apply_highlight(void);
static void slide_back_btn_cb(lv_event_t *e);
static void focus_slide_handle_nav(int8_t detent_delta, bool activate_click);
static void settings_grid_handle_nav(int8_t detent_delta, bool activate_click);

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
            lv_obj_set_style_bg_color(row, lv_color_hex(MIXR_COLOR_FG), 0);
        } else {
            lv_obj_set_style_bg_opa(row, LV_OPA_TRANSP, 0);
        }
        lv_obj_t *lbl = lv_obj_get_child(row, 0);
        if (lbl) {
            lv_color_t fg = lv_color_hex(MIXR_COLOR_FG);
            lv_obj_set_style_text_opa(lbl, LV_OPA_50, 0);
            lv_obj_set_style_text_color(lbl, (int)i == menu_sel ? lv_color_hex(MIXR_COLOR_BG) : fg, 0);
            if ((int)i == menu_sel) {
                lv_obj_set_style_text_opa(lbl, LV_OPA_COVER, 0);
            } else if (menu_nav_current() == MenuScreenId::Settings && cat_items != nullptr && (size_t)i < cat_n
                       && std::strcmp(cat_items[i].id, "restart") == 0) {
                lv_obj_set_style_text_color(lbl, lv_palette_main(LV_PALETTE_PINK), 0);
            }
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
    const MenuItemDef *items = menu_catalog_items(&menu_count);
    if (items == nullptr || menu_count == 0) {
        lv_obj_clean(menu_col);
        return;
    }
    lv_obj_clean(menu_col);
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
        lv_obj_set_style_text_font(lbl, &lv_font_montserrat_22, 0);
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
            lv_label_set_text(menu_title_lbl, "Close");
            break;
        case MenuScreenId::Settings:
            lv_label_set_text(menu_title_lbl, "Settings");
            break;
        case MenuScreenId::Hardware:
            lv_label_set_text(menu_title_lbl, "Hardware");
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
    if (mixr_focus_is_running()) {
        lv_obj_set_style_bg_opa(dim_layer, LV_OPA_TRANSP, 0);
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
    /* Kein transform_scale / lv_text_get_size pro Sekunde — auf dem ESP32 führte das zu Abstürzen
       nach wenigen Sekunden (SW-Renderer + großes Label). Schriftgröße fest in build_menu_overlay. */
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
    if (menu_col == nullptr || menu_special == nullptr || menu_panel == nullptr || focus_run_root == nullptr
        || focus_run_lbl == nullptr) {
        return;
    }

    MenuScreenId s = menu_nav_current();
    if (s == MenuScreenId::Focus && !mixr_focus_is_running() && s_last_rebuilt_screen != MenuScreenId::Focus) {
        s_focus_timer_edit = false;
    }
    s_last_rebuilt_screen = s;

    set_menu_title_for_screen(s);

    if (s == MenuScreenId::Brightness) {
        lv_obj_add_flag(focus_run_root, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(menu_panel, LV_OBJ_FLAG_HIDDEN);
        if (menu_col != nullptr) {
            lv_obj_clean(menu_col);
        }
        lv_obj_add_flag(menu_col, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(menu_special, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(brightness_panel, LV_OBJ_FLAG_HIDDEN);
        uint8_t b = mixr_brightness_get();
        lv_bar_set_value(brightness_bar, b, LV_ANIM_OFF);
        char t[16];
        snprintf(t, sizeof(t), "%u%%", (unsigned)b);
        lv_label_set_text(brightness_val_lbl, t);
        mixr_ui_apply_prefs_to_display();
    } else if (s == MenuScreenId::Focus && mixr_focus_is_running()) {
        lv_obj_add_flag(menu_panel, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(focus_run_root, LV_OBJ_FLAG_HIDDEN);
        mixr_ui_focus_refresh();
        mixr_ui_apply_prefs_to_display();
    } else {
        lv_obj_add_flag(focus_run_root, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(menu_panel, LV_OBJ_FLAG_HIDDEN);
        lv_obj_clear_flag(menu_col, LV_OBJ_FLAG_HIDDEN);
        lv_obj_add_flag(menu_special, LV_OBJ_FLAG_HIDDEN);
        rebuild_list_rows();
        mixr_ui_apply_prefs_to_display();
    }

    menu_sel = 0;
    apply_menu_highlight();
}

static void build_menu_overlay(lv_obj_t *parent_screen)
{
    (void)parent_screen;
    menu_overlay = lv_obj_create(lv_layer_top());
    lv_obj_set_size(menu_overlay, LV_PCT(100), LV_PCT(100));
    apply_solid_screen_bg(menu_overlay);
    lv_obj_set_style_bg_color(menu_overlay, lv_color_hex(MIXR_COLOR_BG), 0);
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
    menu_panel = panel;

    menu_title_lbl = lv_label_create(panel);
    lv_label_set_text(menu_title_lbl, "Menu");
    lv_obj_set_width(menu_title_lbl, LV_PCT(100));
    lv_obj_set_style_text_font(menu_title_lbl, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_align(menu_title_lbl, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_style_text_color(menu_title_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
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
    lv_obj_set_style_bg_color(brightness_bar, lv_color_hex(MIXR_COLOR_BG), LV_PART_MAIN);
    lv_obj_set_style_bg_opa(brightness_bar, LV_OPA_COVER, LV_PART_MAIN);
    lv_obj_set_style_radius(brightness_bar, 8, LV_PART_MAIN);
    lv_obj_set_style_bg_color(brightness_bar, lv_color_hex(MIXR_COLOR_FG), LV_PART_INDICATOR);
    lv_obj_set_style_bg_opa(brightness_bar, LV_OPA_COVER, LV_PART_INDICATOR);
    lv_obj_set_style_radius(brightness_bar, 8, LV_PART_INDICATOR);
    lv_obj_set_style_border_width(brightness_bar, 0, LV_PART_MAIN);
    lv_obj_set_style_border_opa(brightness_bar, LV_OPA_TRANSP, LV_PART_MAIN);
    lv_obj_set_style_outline_width(brightness_bar, 0, LV_PART_MAIN);
    lv_obj_set_style_border_width(brightness_bar, 0, LV_PART_INDICATOR);
    lv_obj_set_style_border_opa(brightness_bar, LV_OPA_TRANSP, LV_PART_INDICATOR);
    lv_obj_set_style_outline_width(brightness_bar, 0, LV_PART_INDICATOR);

    brightness_val_lbl = lv_label_create(brightness_panel);
    lv_label_set_text(brightness_val_lbl, "100%");
    lv_obj_set_style_text_color(brightness_val_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_set_width(brightness_val_lbl, LV_PCT(100));
    lv_obj_set_style_text_align(brightness_val_lbl, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_style_text_font(brightness_val_lbl, &lv_font_montserrat_22, 0);

    /* Vollbild Focus: statisches Dunkelrot, Restzeit unten im unteren Drittel */
    focus_run_root = lv_obj_create(menu_overlay);
    lv_obj_set_size(focus_run_root, TFT_WIDTH, TFT_HEIGHT);
    lv_obj_align(focus_run_root, LV_ALIGN_TOP_LEFT, 0, 0);
    lv_obj_set_style_bg_grad_dir(focus_run_root, LV_GRAD_DIR_NONE, 0);
    lv_obj_set_style_bg_color(focus_run_root, lv_color_hex(MIXR_COLOR_BG), 0);
    lv_obj_set_style_bg_opa(focus_run_root, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(focus_run_root, 0, 0);
    lv_obj_set_style_pad_all(focus_run_root, 0, 0);
    lv_obj_set_style_radius(focus_run_root, 0, 0);
    lv_obj_clear_flag(focus_run_root, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_add_flag(focus_run_root, LV_OBJ_FLAG_HIDDEN);

    focus_run_lbl = lv_label_create(focus_run_root);
    lv_label_set_text(focus_run_lbl, "00:00");
    lv_obj_set_style_text_font(focus_run_lbl, &lv_font_montserrat_32, 0);
    lv_obj_set_style_text_color(focus_run_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_set_width(focus_run_lbl, TFT_WIDTH);
    lv_obj_set_style_text_align(focus_run_lbl, LV_TEXT_ALIGN_CENTER, 0);
    /* Einmal 100 % Skalierung (nur bei Erstellung), nicht bei jedem Tick — siehe mixr_ui_focus_refresh */
    lv_obj_set_style_transform_scale_x(focus_run_lbl, 256, 0);
    lv_obj_set_style_transform_scale_y(focus_run_lbl, 256, 0);
    /* Mitte des unteren Bildschirmdrittels (Mitte + H/3 von Bildmitte) */
    lv_obj_align(focus_run_lbl, LV_ALIGN_CENTER, 0, TFT_HEIGHT / 3);

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

static void apply_solid_screen_bg(lv_obj_t *scr)
{
    lv_obj_set_style_bg_grad_dir(scr, LV_GRAD_DIR_NONE, 0);
    lv_obj_set_style_bg_color(scr, lv_color_hex(MIXR_COLOR_BG), 0);
    lv_obj_set_style_bg_opa(scr, LV_OPA_COVER, 0);
}

static void player_intro_opa_cb(void *var, int32_t v)
{
    lv_obj_t *scr = static_cast<lv_obj_t *>(var);
    if (scr != nullptr) {
        lv_obj_set_style_opa(scr, static_cast<lv_opa_t>(v), 0);
    }
}

static void mixr_ui_player_start_intro(void)
{
    if (screen_player == nullptr) {
        return;
    }
    lv_anim_delete(screen_player, player_intro_opa_cb);
    lv_anim_t a;
    lv_anim_init(&a);
    lv_anim_set_var(&a, screen_player);
    lv_anim_set_exec_cb(&a, player_intro_opa_cb);
    lv_anim_set_values(&a, LV_OPA_TRANSP, LV_OPA_COVER);
    lv_anim_set_duration(&a, 520);
    lv_anim_set_path_cb(&a, lv_anim_path_ease_out);
    lv_anim_start(&a);
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

static void mixr_ui_sync_slide_focus_preset(void)
{
    if (s_sl1_preset_lbl == nullptr) {
        return;
    }
    uint32_t t = mixr_focus_preset_sec_get();
    uint32_t m = t / 60U;
    uint32_t s = t % 60U;
    char buf[24];
    snprintf(buf, sizeof(buf), "%02lu:%02lu", (unsigned long)m, (unsigned long)s);
    lv_label_set_text(s_sl1_preset_lbl, buf);
}

void mixr_ui_set_mute_indicators(bool mic_muted, bool full_mute)
{
    s_mute_mic_on = mic_muted;
    s_mute_full_on = full_mute;
    if (s_mute_mic_img != nullptr) {
        lv_obj_set_style_opa(s_mute_mic_img, mic_muted ? LV_OPA_COVER : LV_OPA_50, 0);
    }
    if (s_mute_head_img != nullptr) {
        lv_obj_set_style_opa(s_mute_head_img, full_mute ? LV_OPA_COVER : LV_OPA_50, 0);
    }
}

static void mixr_ui_update_pager_dots(void)
{
    for (uint8_t i = 0; i < MIXR_SLIDE_COUNT; i++) {
        if (s_pager_dots[i] == nullptr) {
            continue;
        }
        lv_obj_set_style_bg_color(s_pager_dots[i], lv_color_hex(MIXR_COLOR_FG), 0);
        if (i == s_slide_idx) {
            lv_obj_set_size(s_pager_dots[i], 10, 10);
            lv_obj_set_style_radius(s_pager_dots[i], 5, 0);
            lv_obj_set_style_bg_opa(s_pager_dots[i], LV_OPA_COVER, 0);
        } else {
            lv_obj_set_size(s_pager_dots[i], 6, 6);
            lv_obj_set_style_radius(s_pager_dots[i], 3, 0);
            lv_obj_set_style_bg_opa(s_pager_dots[i], LV_OPA_40, 0);
        }
    }
}

static void mixr_ui_carousel_set_scroll_locked(bool locked)
{
    if (s_carousel == nullptr) {
        return;
    }
    if (locked) {
        lv_obj_clear_flag(s_carousel, LV_OBJ_FLAG_SCROLLABLE);
    } else {
        lv_obj_add_flag(s_carousel, LV_OBJ_FLAG_SCROLLABLE);
    }
}

static void mixr_ui_update_carousel_interaction(void)
{
    bool lock = s_slide_edit_active && (s_slide_idx == 1 || s_slide_idx == 2 || s_slide_idx == 3);
    mixr_ui_carousel_set_scroll_locked(lock);
}

static void mixr_ui_refresh_slide2_slide3_layout(void)
{
    if (s_sl2_back_row != nullptr && s_sl2_ph != nullptr) {
        if (s_slide_edit_active && s_slide_idx == 2) {
            lv_obj_clear_flag(s_sl2_back_row, LV_OBJ_FLAG_HIDDEN);
            lv_obj_align_to(s_sl2_ph, s_sl2_back_row, LV_ALIGN_OUT_BOTTOM_MID, 0, 16);
        } else {
            lv_obj_add_flag(s_sl2_back_row, LV_OBJ_FLAG_HIDDEN);
            lv_obj_center(s_sl2_ph);
        }
    }
    if (s_sl3_top != nullptr && s_sl3_grid != nullptr) {
        if (s_slide_edit_active && s_slide_idx == 3) {
            lv_obj_clear_flag(s_sl3_top, LV_OBJ_FLAG_HIDDEN);
            lv_obj_set_size(s_sl3_grid, TFT_WIDTH, MIXR_CAROUSEL_H - MIXR_SLIDE_TOPBAR_H);
            lv_obj_align_to(s_sl3_grid, s_sl3_top, LV_ALIGN_OUT_BOTTOM_MID, 0, 0);
        } else {
            lv_obj_add_flag(s_sl3_top, LV_OBJ_FLAG_HIDDEN);
            lv_obj_set_size(s_sl3_grid, TFT_WIDTH, MIXR_CAROUSEL_H);
            lv_obj_align(s_sl3_grid, LV_ALIGN_TOP_MID, 0, 0);
        }
    }
}

static void mixr_ui_update_slide_back_btn(void)
{
    if (s_slide_back_btn != nullptr) {
        bool show = s_slide_edit_active && s_slide_idx != 0;
        if (show) {
            lv_obj_clear_flag(s_slide_back_btn, LV_OBJ_FLAG_HIDDEN);
        } else {
            lv_obj_add_flag(s_slide_back_btn, LV_OBJ_FLAG_HIDDEN);
        }
    }
    mixr_ui_refresh_slide2_slide3_layout();
}

static void sl1_refresh_focus_visuals(void)
{
    for (int i = 0; i < 3; i++) {
        if (s_sl1_tri_up[i] == nullptr || s_sl1_tri_down[i] == nullptr) {
            continue;
        }
        lv_opa_t opa = (s_sl1_focus_idx == (uint8_t)i) ? LV_OPA_COVER : LV_OPA_30;
        lv_obj_set_style_opa(s_sl1_tri_up[i], opa, 0);
        lv_obj_set_style_opa(s_sl1_tri_down[i], opa, 0);
    }
    if (s_sl1_start_lbl != nullptr) {
        lv_obj_set_style_text_opa(s_sl1_start_lbl, s_sl1_focus_idx == 2 ? LV_OPA_COVER : LV_OPA_50, 0);
    }
}

static void settings_grid_apply_highlight(void)
{
    for (int g = 0; g < 6; g++) {
        if (s_grid_cells[g] == nullptr) {
            continue;
        }
        lv_obj_set_style_border_width(s_grid_cells[g], 0, 0);
        lv_obj_set_style_border_opa(s_grid_cells[g], LV_OPA_TRANSP, 0);
        lv_obj_set_style_outline_width(s_grid_cells[g], 0, 0);
        lv_obj_set_style_outline_opa(s_grid_cells[g], LV_OPA_TRANSP, 0);
    }
    if (!s_slide_edit_active || s_slide_idx != 3) {
        return;
    }
    uint8_t sel = k_settings_grid_nav[s_grid_nav_pos];
    if (s_grid_cells[sel] != nullptr) {
        lv_obj_set_style_outline_width(s_grid_cells[sel], 3, 0);
        lv_obj_set_style_outline_color(s_grid_cells[sel], lv_color_hex(MIXR_COLOR_FG), 0);
        lv_obj_set_style_outline_opa(s_grid_cells[sel], LV_OPA_COVER, 0);
        lv_obj_set_style_outline_pad(s_grid_cells[sel], 4, 0);
    }
}

static void slide_back_btn_cb(lv_event_t *e)
{
    (void)e;
    s_slide_edit_active = false;
    sl1_refresh_focus_visuals();
    settings_grid_apply_highlight();
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
}

static void mixr_ui_open_settings_from_grid_cell(intptr_t cell_idx)
{
    menu_nav_reset();
    switch (cell_idx) {
    case 0:
        menu_nav_push(MenuScreenId::Brightness);
        break;
    case 1:
        menu_nav_push(MenuScreenId::Hardware);
        break;
    case 4:
        menu_nav_push(MenuScreenId::Debug);
        break;
    case 5:
        menu_nav_push(MenuScreenId::Restart);
        break;
    default:
        return;
    }
    menu_sel = 0;
    s_slide_edit_active = false;
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
    lv_obj_clear_flag(menu_overlay, LV_OBJ_FLAG_HIDDEN);
    mixr_ui_menu_rebuild();
}

static void focus_slide_handle_nav(int8_t detent_delta, bool activate_click)
{
    if (detent_delta != 0) {
        if (s_sl1_focus_idx == 0) {
            if (detent_delta > 0) {
                mixr_focus_preset_add_sec(60);
            } else if (mixr_focus_preset_sec_get() > 60U) {
                mixr_focus_preset_add_sec(-60);
            }
            mixr_ui_sync_slide_focus_preset();
            return;
        }
        if (s_sl1_focus_idx == 1) {
            s_sl1_focus_idx = (detent_delta > 0) ? 2 : 0;
            sl1_refresh_focus_visuals();
            return;
        }
        if (s_sl1_focus_idx == 2) {
            s_sl1_focus_idx = (detent_delta > 0) ? 0 : 1;
            sl1_refresh_focus_visuals();
        }
        return;
    }
    if (!activate_click) {
        return;
    }
    switch (s_sl1_focus_idx) {
    case 0:
        s_sl1_focus_idx = 1;
        sl1_refresh_focus_visuals();
        break;
    case 1:
        s_sl1_focus_idx = 2;
        sl1_refresh_focus_visuals();
        break;
    case 2:
        mixr_ui_focus_start_from_slide();
        break;
    default:
        break;
    }
}

static void settings_grid_handle_nav(int8_t detent_delta, bool activate_click)
{
    if (detent_delta != 0) {
        int p = static_cast<int>(s_grid_nav_pos);
        p += (detent_delta > 0) ? 1 : -1;
        const int n = static_cast<int>(k_settings_grid_nav_n);
        p = (p % n + n) % n;
        s_grid_nav_pos = static_cast<uint8_t>(p);
        settings_grid_apply_highlight();
        return;
    }
    if (activate_click) {
        intptr_t g = static_cast<intptr_t>(k_settings_grid_nav[s_grid_nav_pos]);
        mixr_ui_open_settings_from_grid_cell(g);
    }
}

static void carousel_scroll_cb(lv_event_t *e)
{
    if (lv_event_get_code(e) != LV_EVENT_SCROLL_END) {
        return;
    }
    lv_obj_t *c = static_cast<lv_obj_t *>(lv_event_get_target(e));
    lv_coord_t sx = lv_obj_get_scroll_x(c);
    uint32_t idx = (uint32_t)((sx + TFT_WIDTH / 2) / TFT_WIDTH);
    if (idx >= MIXR_SLIDE_COUNT) {
        idx = MIXR_SLIDE_COUNT - 1U;
    }
    s_slide_idx = (uint8_t)idx;
    s_slide_edit_active = false;
    mixr_ui_update_pager_dots();
    sl1_refresh_focus_visuals();
    settings_grid_apply_highlight();
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
    mixr_ui_sync_slide_focus_preset();
}

static void mixr_ui_goto_slide(uint8_t idx)
{
    if (idx >= MIXR_SLIDE_COUNT || s_carousel == nullptr) {
        return;
    }
    if (idx != s_slide_idx) {
        s_slide_edit_active = false;
    }
    s_slide_idx = idx;
    lv_obj_scroll_to_view(s_slide_pages[idx], LV_ANIM_ON);
    mixr_ui_update_pager_dots();
    sl1_refresh_focus_visuals();
    settings_grid_apply_highlight();
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
    mixr_ui_sync_slide_focus_preset();
}

static void mixr_ui_focus_start_from_slide(void)
{
    if (mixr_focus_is_running()) {
        return;
    }
    s_slide_edit_active = false;
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
    menu_nav_reset();
    menu_nav_push(MenuScreenId::Focus);
    mixr_focus_start();
    lv_obj_clear_flag(menu_overlay, LV_OBJ_FLAG_HIDDEN);
    mixr_ui_menu_rebuild();
}

static void settings_grid_btn_cb(lv_event_t *e)
{
    intptr_t id = reinterpret_cast<intptr_t>(lv_event_get_user_data(e));
    if (id == 2 || id == 3) {
        return;
    }
    mixr_ui_open_settings_from_grid_cell(id);
}

static void build_main_carousel(lv_obj_t *parent)
{
    s_carousel = lv_obj_create(parent);
    lv_obj_set_size(s_carousel, TFT_WIDTH, MIXR_CAROUSEL_H);
    lv_obj_align(s_carousel, LV_ALIGN_TOP_LEFT, 0, 0);
    lv_obj_set_style_bg_opa(s_carousel, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(s_carousel, 0, 0);
    lv_obj_set_style_pad_all(s_carousel, 0, 0);
    lv_obj_set_style_outline_width(s_carousel, 0, 0);
    lv_obj_set_flex_flow(s_carousel, LV_FLEX_FLOW_ROW);
    lv_obj_set_flex_align(s_carousel, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_START);
    lv_obj_set_scroll_dir(s_carousel, LV_DIR_HOR);
    lv_obj_set_scroll_snap_x(s_carousel, LV_SCROLL_SNAP_START);
    lv_obj_set_scrollbar_mode(s_carousel, LV_SCROLLBAR_MODE_OFF);
    lv_obj_add_flag(s_carousel, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_add_event_cb(s_carousel, carousel_scroll_cb, LV_EVENT_SCROLL_END, nullptr);

    for (int i = 0; i < MIXR_SLIDE_COUNT; i++) {
        s_slide_pages[i] = lv_obj_create(s_carousel);
        lv_obj_set_size(s_slide_pages[i], TFT_WIDTH, MIXR_CAROUSEL_H);
        lv_obj_set_style_bg_color(s_slide_pages[i], lv_color_hex(MIXR_COLOR_BG), 0);
        lv_obj_set_style_bg_opa(s_slide_pages[i], LV_OPA_COVER, 0);
        lv_obj_set_style_border_width(s_slide_pages[i], 0, 0);
        lv_obj_set_style_pad_all(s_slide_pages[i], 0, 0);
        lv_obj_set_style_outline_width(s_slide_pages[i], 0, 0);
        lv_obj_set_style_radius(s_slide_pages[i], 0, 0);
        lv_obj_clear_flag(s_slide_pages[i], LV_OBJ_FLAG_SCROLLABLE);
    }

    /* --- Slide 0: Cover, Titel, Mute-Anzeige (keine Buttons) --- */
    s_cover_frame = lv_obj_create(s_slide_pages[0]);
    lv_obj_t *cover_frame = s_cover_frame;
    lv_obj_set_size(cover_frame, 240, 240);
    lv_obj_align(cover_frame, LV_ALIGN_TOP_MID, 0, 24);
    lv_obj_set_style_radius(cover_frame, 0, 0);
    lv_obj_set_style_clip_corner(cover_frame, true, 0);
    lv_obj_set_style_border_width(cover_frame, 0, 0);
    lv_obj_set_style_shadow_width(cover_frame, 0, 0);
    /* Dunkler Platzhalter bis Cover-Daten da sind (kein heller „Kasten“ wie Figma-Hintergrund) */
    lv_obj_set_style_bg_color(cover_frame, lv_color_hex(MIXR_COLOR_BG), 0);
    lv_obj_set_style_bg_opa(cover_frame, LV_OPA_COVER, 0);
    lv_obj_clear_flag(cover_frame, LV_OBJ_FLAG_SCROLLABLE);

    ui_cover = lv_image_create(cover_frame);
    lv_obj_set_size(ui_cover, 240, 240);
    lv_obj_center(ui_cover);

    ui_title = lv_label_create(s_slide_pages[0]);
    lv_label_set_text(ui_title, s_pending_title);
#if defined(LV_FONT_MONTSERRAT_20) && LV_FONT_MONTSERRAT_20
    lv_obj_set_style_text_font(ui_title, &lv_font_montserrat_20, 0);
#else
    lv_obj_set_style_text_font(ui_title, &lv_font_montserrat_22, 0);
#endif
    lv_obj_set_style_text_color(ui_title, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_label_set_long_mode(ui_title, LV_LABEL_LONG_SCROLL_CIRCULAR);
    lv_obj_set_width(ui_title, 228);
    {
        const lv_font_t *tf = lv_obj_get_style_text_font(ui_title, LV_PART_MAIN);
        lv_coord_t lh = (tf != nullptr) ? lv_font_get_line_height(tf) : 26;
        lv_obj_set_height(ui_title, lh + 6);
    }
    lv_obj_set_style_text_align(ui_title, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_align_to(ui_title, cover_frame, LV_ALIGN_OUT_BOTTOM_MID, 0, 20);

    ui_artist = lv_label_create(s_slide_pages[0]);
    lv_label_set_text(ui_artist, s_pending_artist);
#if defined(LV_FONT_MONTSERRAT_20) && LV_FONT_MONTSERRAT_20
    lv_obj_set_style_text_font(ui_artist, &lv_font_montserrat_20, 0);
#else
    lv_obj_set_style_text_font(ui_artist, &lv_font_montserrat_22, 0);
#endif
    lv_obj_set_style_text_color(ui_artist, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_label_set_long_mode(ui_artist, LV_LABEL_LONG_SCROLL_CIRCULAR);
    lv_obj_set_width(ui_artist, 228);
    {
        const lv_font_t *af = lv_obj_get_style_text_font(ui_artist, LV_PART_MAIN);
        lv_coord_t lh = (af != nullptr) ? lv_font_get_line_height(af) : 20;
        lv_obj_set_height(ui_artist, lh + 6);
    }
    lv_obj_set_style_text_align(ui_artist, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_align_to(ui_artist, ui_title, LV_ALIGN_OUT_BOTTOM_MID, 0, 6);

    lv_obj_t *mute_row = lv_obj_create(s_slide_pages[0]);
    lv_obj_set_width(mute_row, LV_PCT(100));
    lv_obj_set_height(mute_row, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_opa(mute_row, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(mute_row, 0, 0);
    lv_obj_set_style_pad_ver(mute_row, 8, 0);
    lv_obj_set_flex_flow(mute_row, LV_FLEX_FLOW_ROW);
    lv_obj_set_flex_align(mute_row, LV_FLEX_ALIGN_SPACE_EVENLY, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_align_to(mute_row, ui_artist, LV_ALIGN_OUT_BOTTOM_MID, 0, 24);
    lv_obj_clear_flag(mute_row, LV_OBJ_FLAG_CLICKABLE);

    s_mute_mic_img = lv_image_create(mute_row);
    lv_image_set_src(s_mute_mic_img, &img_mic);
    lv_obj_center(s_mute_mic_img);

    s_mute_head_img = lv_image_create(mute_row);
    lv_image_set_src(s_mute_head_img, &img_headphones);
    lv_obj_center(s_mute_head_img);
    mixr_ui_set_mute_indicators(false, false);

    err_banner_lbl = lv_label_create(s_slide_pages[0]);
    lv_label_set_text(err_banner_lbl, "");
    lv_obj_set_style_text_font(err_banner_lbl, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(err_banner_lbl, lv_palette_main(LV_PALETTE_RED), 0);
    lv_label_set_long_mode(err_banner_lbl, LV_LABEL_LONG_DOT);
    lv_obj_set_width(err_banner_lbl, 228);
    lv_obj_align(err_banner_lbl, LV_ALIGN_BOTTOM_MID, 0, -12);
    lv_obj_add_flag(err_banner_lbl, LV_OBJ_FLAG_HIDDEN);

    /* --- Slide 1: Focus (ClockIndicator / Bell assets) --- */
    lv_obj_t *sl1 = s_slide_pages[1];
    lv_obj_set_size(sl1, TFT_WIDTH, MIXR_CAROUSEL_H);
    lv_obj_set_style_bg_opa(sl1, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(sl1, 0, 0);
    lv_obj_set_style_pad_ver(sl1, 16, 0);
    lv_obj_set_flex_flow(sl1, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(sl1, LV_FLEX_ALIGN_SPACE_BETWEEN, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_clear_flag(sl1, LV_OBJ_FLAG_SCROLLABLE);

    lv_obj_t *blk0 = lv_obj_create(sl1);
    lv_obj_set_width(blk0, LV_PCT(100));
    lv_obj_set_height(blk0, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_opa(blk0, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(blk0, 0, 0);
    lv_obj_set_flex_flow(blk0, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(blk0, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_row(blk0, 2, 0);

    s_sl1_tri_up[0] = lv_image_create(blk0);
    lv_image_set_src(s_sl1_tri_up[0], &img_clock_indicator_up);
    lv_image_set_pivot(s_sl1_tri_up[0], 16, 16);
    lv_obj_center(s_sl1_tri_up[0]);

    s_sl1_preset_lbl = lv_label_create(blk0);
    lv_obj_set_style_text_font(s_sl1_preset_lbl, &lv_font_montserrat_28, 0);
    lv_obj_set_style_text_color(s_sl1_preset_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_label_set_text(s_sl1_preset_lbl, "25:00");
    lv_obj_set_style_text_align(s_sl1_preset_lbl, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_set_width(s_sl1_preset_lbl, LV_PCT(100));
    mixr_ui_sync_slide_focus_preset();

    s_sl1_tri_down[0] = lv_image_create(blk0);
    lv_image_set_src(s_sl1_tri_down[0], &img_clock_indicator_down);
    lv_image_set_pivot(s_sl1_tri_down[0], 16, 16);
    lv_obj_center(s_sl1_tri_down[0]);

    lv_obj_t *blk1 = lv_obj_create(sl1);
    lv_obj_set_width(blk1, LV_PCT(100));
    lv_obj_set_height(blk1, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_opa(blk1, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(blk1, 0, 0);
    lv_obj_set_flex_flow(blk1, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(blk1, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_row(blk1, 2, 0);

    s_sl1_tri_up[1] = lv_image_create(blk1);
    lv_image_set_src(s_sl1_tri_up[1], &img_clock_indicator_up);
    lv_image_set_pivot(s_sl1_tri_up[1], 16, 16);
    lv_obj_center(s_sl1_tri_up[1]);

    lv_obj_t *bell_wrap = lv_obj_create(blk1);
    lv_obj_set_size(bell_wrap, 120, 120);
    lv_obj_set_style_bg_opa(bell_wrap, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(bell_wrap, 0, 0);
    lv_obj_set_style_pad_all(bell_wrap, 0, 0);
    lv_obj_clear_flag(bell_wrap, LV_OBJ_FLAG_SCROLLABLE);

    lv_obj_t *bell_img = lv_image_create(bell_wrap);
    lv_image_set_src(bell_img, &img_bell);
    lv_obj_center(bell_img);

    s_sl1_tri_down[1] = lv_image_create(blk1);
    lv_image_set_src(s_sl1_tri_down[1], &img_clock_indicator_down);
    lv_image_set_pivot(s_sl1_tri_down[1], 16, 16);
    lv_obj_center(s_sl1_tri_down[1]);

    lv_obj_t *blk2 = lv_obj_create(sl1);
    lv_obj_set_width(blk2, LV_PCT(100));
    lv_obj_set_height(blk2, LV_SIZE_CONTENT);
    lv_obj_set_style_bg_opa(blk2, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(blk2, 0, 0);
    lv_obj_set_flex_flow(blk2, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(blk2, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_row(blk2, 2, 0);

    s_sl1_tri_up[2] = lv_image_create(blk2);
    lv_image_set_src(s_sl1_tri_up[2], &img_clock_indicator_up);
    lv_image_set_pivot(s_sl1_tri_up[2], 16, 16);
    lv_obj_center(s_sl1_tri_up[2]);

    s_sl1_start_lbl = lv_label_create(blk2);
    lv_label_set_text(s_sl1_start_lbl, "Start");
    lv_obj_set_style_text_font(s_sl1_start_lbl, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_color(s_sl1_start_lbl, lv_color_hex(MIXR_COLOR_FG), 0);

    s_sl1_tri_down[2] = lv_image_create(blk2);
    lv_image_set_src(s_sl1_tri_down[2], &img_clock_indicator_down);
    lv_image_set_pivot(s_sl1_tri_down[2], 16, 16);
    lv_obj_center(s_sl1_tri_down[2]);

    sl1_refresh_focus_visuals();

    /* --- Slide 2: Platzhalter + „Back“ (sichtbar im Bearbeitungsmodus) --- */
    lv_obj_t *sl2 = s_slide_pages[2];
    s_sl2_back_row = lv_obj_create(sl2);
    lv_obj_set_size(s_sl2_back_row, TFT_WIDTH, MIXR_SLIDE_TOPBAR_H);
    lv_obj_align(s_sl2_back_row, LV_ALIGN_TOP_LEFT, 0, 0);
    lv_obj_set_style_bg_opa(s_sl2_back_row, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(s_sl2_back_row, 0, 0);
    lv_obj_set_style_pad_all(s_sl2_back_row, 0, 0);
    lv_obj_set_style_pad_left(s_sl2_back_row, 2, 0);
    lv_obj_add_flag(s_sl2_back_row, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_t *sl2_back_lbl = lv_label_create(s_sl2_back_row);
    lv_label_set_text(sl2_back_lbl, "Back");
    lv_obj_set_style_text_font(sl2_back_lbl, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_color(sl2_back_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_align(sl2_back_lbl, LV_ALIGN_LEFT_MID, 4, 0);
    lv_obj_add_event_cb(s_sl2_back_row, slide_back_btn_cb, LV_EVENT_CLICKED, nullptr);
    lv_obj_add_flag(s_sl2_back_row, LV_OBJ_FLAG_HIDDEN);

    s_sl2_ph = lv_label_create(sl2);
    lv_label_set_text(s_sl2_ph, "Platz halter");
    lv_obj_set_style_text_font(s_sl2_ph, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_color(s_sl2_ph, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_label_set_long_mode(s_sl2_ph, LV_LABEL_LONG_WRAP);
    lv_obj_set_width(s_sl2_ph, 200);
    lv_obj_set_style_text_align(s_sl2_ph, LV_TEXT_ALIGN_CENTER, 0);
    lv_obj_center(s_sl2_ph);

    /* --- Slide 3: Settings-Raster + Kopfzeile „Back“ / Titel --- */
    lv_obj_t *sl3 = s_slide_pages[3];
    s_sl3_top = lv_obj_create(sl3);
    lv_obj_set_width(s_sl3_top, TFT_WIDTH);
    lv_obj_set_height(s_sl3_top, MIXR_SLIDE_TOPBAR_H);
    lv_obj_align(s_sl3_top, LV_ALIGN_TOP_MID, 0, 0);
    lv_obj_set_style_bg_opa(s_sl3_top, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(s_sl3_top, 0, 0);
    lv_obj_set_style_pad_all(s_sl3_top, 0, 0);
    lv_obj_set_style_pad_hor(s_sl3_top, 2, 0);
    lv_obj_set_flex_flow(s_sl3_top, LV_FLEX_FLOW_ROW);
    lv_obj_set_flex_align(s_sl3_top, LV_FLEX_ALIGN_SPACE_BETWEEN, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_clear_flag(s_sl3_top, LV_OBJ_FLAG_SCROLLABLE);

    lv_obj_t *sl3_back = lv_obj_create(s_sl3_top);
    lv_obj_set_size(sl3_back, 100, 36);
    lv_obj_set_style_bg_opa(sl3_back, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(sl3_back, 0, 0);
    lv_obj_add_flag(sl3_back, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_t *sl3_back_lbl = lv_label_create(sl3_back);
    lv_label_set_text(sl3_back_lbl, "Back");
    lv_obj_set_style_text_font(sl3_back_lbl, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_color(sl3_back_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_center(sl3_back_lbl);
    lv_obj_add_event_cb(sl3_back, slide_back_btn_cb, LV_EVENT_CLICKED, nullptr);

    lv_obj_t *set_title = lv_label_create(s_sl3_top);
    lv_label_set_text(set_title, "Settings");
    lv_obj_set_style_text_font(set_title, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_color(set_title, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_set_style_pad_right(set_title, 4, 0);

    lv_obj_add_flag(s_sl3_top, LV_OBJ_FLAG_HIDDEN);

    s_sl3_grid = lv_obj_create(sl3);
    lv_obj_set_size(s_sl3_grid, TFT_WIDTH, MIXR_CAROUSEL_H);
    lv_obj_align(s_sl3_grid, LV_ALIGN_TOP_MID, 0, 0);
    lv_obj_set_style_bg_opa(s_sl3_grid, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(s_sl3_grid, 0, 0);
    lv_obj_set_style_pad_all(s_sl3_grid, 0, 0);
    lv_obj_set_flex_flow(s_sl3_grid, LV_FLEX_FLOW_ROW_WRAP);
    lv_obj_set_flex_align(s_sl3_grid, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_START);
    lv_obj_set_style_pad_row(s_sl3_grid, 0, 0);
    lv_obj_set_style_pad_column(s_sl3_grid, 0, 0);

    static const lv_image_dsc_t *const grid_img_src[6] = {
        &img_brightness,
        &img_hardware,
        nullptr,
        nullptr,
        &img_debug,
        &img_restart,
    };
    for (int g = 0; g < 6; g++) {
        lv_coord_t cell_w = (TFT_WIDTH / 2);
        lv_obj_t *cell = lv_obj_create(s_sl3_grid);
        s_grid_cells[g] = cell;
        lv_obj_set_size(cell, cell_w, 128);
        lv_obj_set_style_bg_opa(cell, LV_OPA_TRANSP, 0);
        lv_obj_set_style_border_width(cell, 0, 0);
        lv_obj_set_style_pad_all(cell, 0, 0);
        lv_obj_set_style_outline_width(cell, 0, 0);
        lv_obj_set_style_outline_opa(cell, LV_OPA_TRANSP, 0);
        lv_obj_clear_flag(cell, LV_OBJ_FLAG_SCROLLABLE);
        if (g == 2 || g == 3) {
            lv_obj_add_flag(cell, LV_OBJ_FLAG_HIDDEN);
            continue;
        }
        lv_obj_add_flag(cell, LV_OBJ_FLAG_CLICKABLE);
        lv_obj_add_event_cb(cell, settings_grid_btn_cb, LV_EVENT_CLICKED, reinterpret_cast<void *>(static_cast<intptr_t>(g)));

        lv_obj_t *ico = lv_image_create(cell);
        lv_image_set_src(ico, grid_img_src[g]);
        lv_obj_center(ico);
    }
    settings_grid_apply_highlight();

    /* Pager-Punkte */
    s_pager_row = lv_obj_create(parent);
    lv_obj_set_size(s_pager_row, TFT_WIDTH, MIXR_PAGER_H);
    lv_obj_align(s_pager_row, LV_ALIGN_BOTTOM_MID, 0, 0);
    lv_obj_set_style_bg_color(s_pager_row, lv_color_hex(MIXR_COLOR_BG), 0);
    lv_obj_set_style_bg_opa(s_pager_row, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(s_pager_row, 0, 0);
    lv_obj_set_flex_flow(s_pager_row, LV_FLEX_FLOW_ROW);
    lv_obj_set_flex_align(s_pager_row, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_column(s_pager_row, 10, 0);
    lv_obj_clear_flag(s_pager_row, LV_OBJ_FLAG_SCROLLABLE);

    for (uint8_t i = 0; i < MIXR_SLIDE_COUNT; i++) {
        s_pager_dots[i] = lv_obj_create(s_pager_row);
        lv_obj_set_size(s_pager_dots[i], 8, 8);
        lv_obj_set_style_radius(s_pager_dots[i], 4, 0);
    }
    s_slide_idx = 0;
    mixr_ui_update_pager_dots();
    mixr_ui_refresh_slide2_slide3_layout();
}

void mixr_ui_main_navigate(int8_t detent_delta, bool activate_click)
{
    if (s_carousel == nullptr) {
        return;
    }

    if (!s_slide_edit_active) {
        if (activate_click) {
            s_slide_edit_active = true;
            if (s_slide_idx == 1) {
                s_sl1_focus_idx = 0;
                sl1_refresh_focus_visuals();
            } else if (s_slide_idx == 3) {
                s_grid_nav_pos = 0;
                settings_grid_apply_highlight();
            }
            mixr_ui_update_carousel_interaction();
            mixr_ui_update_slide_back_btn();
            return;
        }
        if (detent_delta != 0) {
            int32_t ni = (int32_t)s_slide_idx;
            ni += (detent_delta > 0) ? 1 : -1;
            if (ni < 0) {
                ni = MIXR_SLIDE_COUNT - 1;
            }
            if (ni >= MIXR_SLIDE_COUNT) {
                ni = 0;
            }
            mixr_ui_goto_slide((uint8_t)ni);
        }
        return;
    }

    /* Slide „aktiv“: Encoder bedient Optionen; Cover (0): Klick beendet Aktivierung (kein altes Menü) */
    switch (s_slide_idx) {
    case 0:
        if (activate_click) {
            s_slide_edit_active = false;
            mixr_ui_update_carousel_interaction();
            mixr_ui_update_slide_back_btn();
        } else if (detent_delta != 0) {
            int32_t ni = (int32_t)s_slide_idx;
            ni += (detent_delta > 0) ? 1 : -1;
            if (ni < 0) {
                ni = MIXR_SLIDE_COUNT - 1;
            }
            if (ni >= MIXR_SLIDE_COUNT) {
                ni = 0;
            }
            mixr_ui_goto_slide((uint8_t)ni);
        }
        break;
    case 1:
        if (!mixr_focus_is_running()) {
            focus_slide_handle_nav(detent_delta, activate_click);
        }
        break;
    case 2:
        if (detent_delta != 0) {
            int32_t ni = (int32_t)s_slide_idx;
            ni += (detent_delta > 0) ? 1 : -1;
            if (ni < 0) {
                ni = MIXR_SLIDE_COUNT - 1;
            }
            if (ni >= MIXR_SLIDE_COUNT) {
                ni = 0;
            }
            mixr_ui_goto_slide((uint8_t)ni);
        }
        break;
    case 3:
        settings_grid_handle_nav(detent_delta, activate_click);
        break;
    default:
        break;
    }
}

void mixr_ui_init(lv_display_t *disp, uint8_t *cover_buf, uint32_t cover_bytes, uint32_t boot_count)
{
    s_cover_buf = cover_buf;
    s_cover_bytes = cover_bytes;

    screen_loading = lv_obj_create(nullptr);
    lv_obj_set_size(screen_loading, TFT_WIDTH, TFT_HEIGHT);
    apply_solid_screen_bg(screen_loading);
    lv_obj_set_style_pad_all(screen_loading, 0, 0);
    lv_obj_set_style_border_width(screen_loading, 0, 0);
    lv_obj_set_style_outline_width(screen_loading, 0, 0);

    lv_obj_t *lbl = lv_label_create(screen_loading);
    lv_label_set_text(lbl, "Connect USB...");
    lv_obj_set_style_text_color(lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_set_style_text_font(lbl, &lv_font_montserrat_22, 0);
    lv_obj_align(lbl, LV_ALIGN_CENTER, 0, 0);

    screen_player = lv_obj_create(nullptr);
    lv_obj_set_size(screen_player, TFT_WIDTH, TFT_HEIGHT);
    apply_solid_screen_bg(screen_player);
    lv_obj_set_style_bg_color(screen_player, lv_color_hex(MIXR_COLOR_BG), 0);
    lv_obj_set_style_bg_opa(screen_player, LV_OPA_COVER, 0);
    lv_obj_set_style_pad_all(screen_player, 0, 0);
    lv_obj_set_style_border_width(screen_player, 0, 0);
    lv_obj_set_style_outline_width(screen_player, 0, 0);

    build_main_carousel(screen_player);

    s_slide_back_btn = lv_obj_create(screen_player);
    lv_obj_set_size(s_slide_back_btn, 52, 44);
    lv_obj_align(s_slide_back_btn, LV_ALIGN_TOP_RIGHT, -4, 4);
    lv_obj_set_style_bg_opa(s_slide_back_btn, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(s_slide_back_btn, 0, 0);
    lv_obj_add_flag(s_slide_back_btn, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_t *back_lbl = lv_label_create(s_slide_back_btn);
    lv_label_set_text(back_lbl, "<<");
    lv_obj_set_style_text_font(back_lbl, &lv_font_montserrat_22, 0);
    lv_obj_set_style_text_color(back_lbl, lv_color_hex(MIXR_COLOR_FG), 0);
    lv_obj_center(back_lbl);
    lv_obj_add_event_cb(s_slide_back_btn, slide_back_btn_cb, LV_EVENT_CLICKED, nullptr);
    lv_obj_add_flag(s_slide_back_btn, LV_OBJ_FLAG_HIDDEN);

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
    if (connected) {
        lv_anim_delete(screen_player, nullptr);
        lv_obj_set_style_opa(screen_player, LV_OPA_TRANSP, 0);
        lv_screen_load(screen_player);
        mixr_ui_player_start_intro();
    } else {
        lv_anim_delete(screen_player, nullptr);
        lv_obj_set_style_opa(screen_player, LV_OPA_COVER, 0);
        lv_screen_load(screen_loading);
    }
    if (mixr_ui_is_menu_open()
        && (menu_nav_current() == MenuScreenId::Debug || menu_nav_current() == MenuScreenId::Hardware)) {
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
            if (ui_title != nullptr) {
                lv_label_set_text(ui_title, s_pending_title);
            }
            break;
        case PktType::SONG_ARTIST:
            std::strncpy(s_pending_artist, msg->payload.text, sizeof(s_pending_artist) - 1);
            s_pending_artist[sizeof(s_pending_artist) - 1] = '\0';
            if (ui_artist != nullptr) {
                lv_label_set_text(ui_artist, s_pending_artist);
            }
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
    mixr_ui_sync_slide_focus_preset();
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
    s_slide_edit_active = false;
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
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
    s_slide_edit_active = false;
    mixr_ui_update_carousel_interaction();
    mixr_ui_update_slide_back_btn();
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
            menu_nav_reset();
            mixr_ui_enter_song_view_from_menu();
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
                menu_nav_reset();
                mixr_ui_enter_song_view_from_menu();
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
