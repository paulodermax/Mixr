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
lv_obj_t *ui_title;
lv_obj_t *ui_artist;

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

// Titel
    ui_title = lv_label_create(screen_player); // KEIN 'lv_obj_t *' davor!
    lv_label_set_text(ui_title, "Aktueller Song");
    lv_obj_set_style_text_font(ui_title, &lv_font_montserrat_20, 0);
    lv_obj_set_style_text_color(ui_title, lv_color_white(), 0);
    lv_obj_align(ui_title, LV_ALIGN_CENTER, 0, 100);

    // Künstler
    ui_artist = lv_label_create(screen_player); // KEIN 'lv_obj_t *' davor!
    lv_label_set_text(ui_artist, "Kuenstler Name");
    lv_obj_set_style_text_color(ui_artist, lv_palette_lighten(LV_PALETTE_GREY, 1), 0);
    lv_obj_align(ui_artist, LV_ALIGN_CENTER, 0, 130);
}

/**************************************************************
 * FreeRTOS Queue & Kommunikation
 **************************************************************/

#include "protocol.h"

QueueHandle_t ui_queue;

void comm_task(void *pvParameters) {
    uint8_t rx_byte;
    uint8_t state = 0;
    uint8_t len = 0;
    uint8_t type = 0;
    uint8_t payload[128];
    uint8_t payload_idx = 0;
    uint8_t crc = 0;

    while (1) {
        if (usb_serial_jtag_read_bytes(&rx_byte, 1, portMAX_DELAY) == 1) {
            switch (state) {
                case 0: 
                    if (rx_byte == PKT_START_BYTE) {
                        state = 1;
                        crc = 0;
                    }
                    break;
                case 1: 
                    len = rx_byte;
                    crc ^= rx_byte;
                    state = 2;
                    break;
                case 2: 
                    type = rx_byte;
                    crc ^= rx_byte;
                    payload_idx = 0;
                    state = (len > 0) ? 3 : 4;
                    break;
                case 3: 
                    payload[payload_idx++] = rx_byte;
                    crc ^= rx_byte;
                    if (payload_idx == len) state = 4;
                    break;
                case 4: 
                    if (rx_byte == crc) {
                        UiMessage msg;
                        msg.type = static_cast<PktType>(type);
                        
                        if (msg.type == PktType::SONG_TITLE || msg.type == PktType::SONG_ARTIST) {
                            uint8_t copy_len = (len < sizeof(msg.payload.text) - 1) ? len : sizeof(msg.payload.text) - 1;
                            memcpy(msg.payload.text, payload, copy_len);
                            msg.payload.text[copy_len] = '\0';
                        } else if (msg.type == PktType::SLIDER_VALS && len == 4) {
                            memcpy(msg.payload.slider_values, payload, 4);
                        } else if (msg.type == PktType::BTN_CMD && len == 1) {
                            msg.payload.command = static_cast<BtnCmd>(payload[0]);
                        }
                        
                        xQueueSend(ui_queue, &msg, 0);
                    }
                    state = 0;
                    break;
            }
        }
    }
}


/**************************************************************
 * MAIN APPLICATION
 **************************************************************/

extern "C" void app_main(void)
{
    rm67162_init();
    // USB Serial JTAG Treiber installieren
    usb_serial_jtag_driver_config_t usb_config = {
        .tx_buffer_size = 256,
        .rx_buffer_size = 256
    };
    usb_serial_jtag_driver_install(&usb_config);
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

    // FEHLTE: Queue und Kommunikations-Task starten
    ui_queue = xQueueCreate(10, sizeof(UiMessage));
    xTaskCreate(comm_task, "comm_task", 4096, NULL, 5, NULL);

    // UI nur EINMAL erstellen
    create_loading_screen();
    create_player_screen();
    lv_screen_load(screen_loading);

    bool last_usb_state = false;
    UiMessage incoming_msg;

    while (1) {
        bool current_usb_state = usb_serial_jtag_is_connected();
        
        if (current_usb_state != last_usb_state) {
            lv_screen_load(current_usb_state ? screen_player : screen_loading);
            last_usb_state = current_usb_state;
        }

        if (xQueueReceive(ui_queue, &incoming_msg, 0) == pdTRUE) {
            switch (incoming_msg.type) {
                case PktType::SONG_TITLE:
                    if (ui_title) {
                        lv_label_set_text(ui_title, incoming_msg.payload.text);
                    }
                    break;

                case PktType::SONG_ARTIST:
                    if (ui_artist) {
                        lv_label_set_text(ui_artist, incoming_msg.payload.text);
                    }
                    break;

                case PktType::SLIDER_VALS:
                    ESP_LOGI(TAG, "S1:%d S2:%d S3:%d S4:%d", 
                             incoming_msg.payload.slider_values[0],
                             incoming_msg.payload.slider_values[1],
                             incoming_msg.payload.slider_values[2],
                             incoming_msg.payload.slider_values[3]);
                    break;

                case PktType::BTN_CMD:
                    ESP_LOGI(TAG, "CMD ID: %d", static_cast<int>(incoming_msg.payload.command));
                    break;
            }
        }

        lv_timer_handler();
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}