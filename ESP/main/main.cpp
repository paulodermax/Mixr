#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_timer.h"
#include "esp_log.h"
#include "lvgl.h"
#include "rm67162.h"
#include "FT3168.h"
#include "driver/usb_serial_jtag.h"

static const char *TAG = "MIX_MAIN";

// Instanzen & Globale Variablen
FT3168 touch(I2C_SDA, I2C_SCL, -1, -1);
lv_obj_t *screen_loading;
lv_obj_t *screen_player;

/**************************************************************
 * LVGL CALLBACKS & HELPER (Müssen VOR app_main stehen)
 **************************************************************/

void my_disp_flush(lv_display_t *disp, const lv_area_t *area, uint8_t *px_map) {
    lcd_PushColors(area->x1, area->y1, (area->x2 - area->x1 + 1), (area->y2 - area->y1 + 1), (uint16_t *)px_map);
    lv_display_flush_ready(disp);
}

void my_touchpad_read(lv_indev_t *indev, lv_indev_data_t *data) {
    uint16_t x, y;
    uint8_t g;
    if (touch.getTouch(&x, &y, &g)) {
        data->state = LV_INDEV_STATE_PRESSED;
        data->point.x = x;
        data->point.y = y;
    } else {
        data->state = LV_INDEV_STATE_RELEASED;
    }
}

static void lv_tick_task(void *arg) { lv_tick_inc(5); }

/**************************************************************
 * UI SCREENS
 **************************************************************/

void create_loading_screen() {
    screen_loading = lv_obj_create(NULL);
    lv_obj_set_style_bg_color(screen_loading, lv_color_black(), 0);

    lv_obj_t *spinner = lv_spinner_create(screen_loading);
    lv_obj_set_size(spinner, 60, 60);
    lv_obj_center(spinner);

    lv_obj_t *label = lv_label_create(screen_loading);
    lv_label_set_text(label, "Warte auf USB-C...");
    lv_obj_set_style_text_color(label, lv_color_white(), 0);
    lv_obj_align(label, LV_ALIGN_CENTER, 0, 50);
}

void create_player_screen() {
    screen_player = lv_obj_create(NULL);
    lv_obj_set_style_bg_color(screen_player, lv_color_black(), 0);

    lv_obj_t *cover = lv_obj_create(screen_player);
    lv_obj_set_size(cover, 180, 180);
    lv_obj_align(cover, LV_ALIGN_TOP_MID, 0, 40);
    lv_obj_set_style_bg_color(cover, lv_palette_main(LV_PALETTE_GREY), 0);
    lv_obj_set_style_radius(cover, 10, 0);

    lv_obj_t *title = lv_label_create(screen_player);
    lv_label_set_text(title, "Aktueller Song");
    
    // Falls Montserrat 20 im Menüconfig noch nicht aktiv ist, 
    // nimm kurzzeitig &lv_font_montserrat_14 zum Testen
    lv_obj_set_style_text_font(title, &lv_font_montserrat_20, 0);
    
    lv_obj_set_style_text_color(title, lv_color_white(), 0);
    lv_obj_align(title, LV_ALIGN_CENTER, 0, 100);

    lv_obj_t *artist = lv_label_create(screen_player);
    lv_label_set_text(artist, "Kuenstler Name");
    lv_obj_set_style_text_color(artist, lv_palette_lighten(LV_PALETTE_GREY, 1), 0);
    lv_obj_align(artist, LV_ALIGN_CENTER, 0, 130);
}

/**************************************************************
 * MAIN APPLICATION
 **************************************************************/

extern "C" void app_main(void)
{
    rm67162_init();
    lcd_setRotation(0);
    touch.begin();

    lv_init();
    size_t buf_size = (TFT_WIDTH * TFT_HEIGHT) / 10;
    void *buf = heap_caps_malloc(buf_size * sizeof(lv_color_t), MALLOC_CAP_SPIRAM);
    
    lv_display_t *disp = lv_display_create(TFT_WIDTH, TFT_HEIGHT);
    lv_display_set_buffers(disp, buf, NULL, buf_size * sizeof(lv_color_t), LV_DISPLAY_RENDER_MODE_PARTIAL);
    lv_display_set_flush_cb(disp, my_disp_flush);

    lv_indev_t *indev = lv_indev_create();
    lv_indev_set_type(indev, LV_INDEV_TYPE_POINTER);
    lv_indev_set_read_cb(indev, my_touchpad_read);

    // Timer mit allen Initialisierern (behebt die Warnung)
    const esp_timer_create_args_t timer_args = {
        .callback = &lv_tick_task,
        .arg = NULL,
        .dispatch_method = ESP_TIMER_TASK,
        .name = "tick",
        .skip_unhandled_events = false
    };
    esp_timer_handle_t timer;
    esp_timer_create(&timer_args, &timer);
    esp_timer_start_periodic(timer, 5000);

    create_loading_screen();
    create_player_screen();
    lv_screen_load(screen_loading);

    bool last_usb_state = false;

    while (1) {
        bool current_usb_state = usb_serial_jtag_is_connected();
        if (current_usb_state != last_usb_state) {
            if (current_usb_state) {
                lv_screen_load(screen_player);
            } else {
                lv_screen_load(screen_loading);
            }
            last_usb_state = current_usb_state;
        }
        lv_timer_handler();
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}