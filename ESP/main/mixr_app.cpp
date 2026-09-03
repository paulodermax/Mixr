/*
 * Mixr — Hauptprogramm.
 *
 * Kommunikation mit dem PC läuft über mixr_link (Backend per Kconfig: USB-HID-Composite oder
 * USB-Serial/JTAG) und mixr_proto (Frame-Handler). Diese Datei kümmert sich um Hardware, LVGL,
 * Fader/Tasten/Encoder und die Hauptschleife.
 *
 * Threading: alle lv_*-Aufrufe laufen ausschließlich in mixr_app_run() (Main-Task). Timer-Callbacks
 * und der comm_task des Links kommunizieren mit der UI nur über ui_queue.
 */

#include "board_pins.h"
#include "encoder_ky040.hpp"
#include "mixr_fw_update.hpp"
#include "mixr_link.hpp"
#include "mixr_log_stream.hpp"
#include "mixr_proto.hpp"
#include "mixr_settings.hpp"
#include "protocol.h"
#include "rm67162.h"
#include "ui_mixr.hpp"

#include "FT3168.h"
#include "driver/gpio.h"
#include "driver/spi_master.h"
#include "esp_app_desc.h"
#include "esp_attr.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "lvgl.h"
#include "mixr_ui_font.h"
#if LV_USE_THEME_DEFAULT
#include "src/themes/default/lv_theme_default.h"
#endif
#include "pins_config.h"

#include <cstring>
#include <stdint.h>

static const char *TAG = "mixr_app";

RTC_DATA_ATTR static uint32_t s_mixr_boot_count;

FT3168 touch(I2C_SDA, I2C_SCL, -1, -1);
static spi_device_handle_t spi_mcp;
/** MCP3008 wird aus dem Controls-Timer (esp_timer-Task) und aus der UI (Resync) gelesen. */
static SemaphoreHandle_t s_spi_mutex;

static uint8_t *img_buf = nullptr;

static QueueHandle_t ui_queue;
static Ky040Encoder g_encoder;

/** USB-Slider/Buttons: vom Timer statt aus main, damit es bei langem LVGL-Cover-Draw weiterläuft */
static uint8_t s_last_sliders[MIXR_SLIDER_COUNT] = {0};
static uint8_t s_last_buttons[MIXR_BUTTON_COUNT] = {1, 1, 1, 1, 1};
/** Letzter BTN_CMD pro Taste (esp_timer_get_time, µs) — Sperre gegen Prellen/Doppeltreffer */
static int64_t s_last_btn_cmd_us[MIXR_BUTTON_COUNT] = {0};

#ifndef MIXR_BUTTON_DEBOUNCE_US
#define MIXR_BUTTON_DEBOUNCE_US (50 * 1000) /* 50 ms; optional per -DMIXR_BUTTON_DEBOUNCE_US=… überschreiben */
#endif

#ifndef MIXR_TX_LED_PULSE_US
#define MIXR_TX_LED_PULSE_US 4000 /* ~4 ms sichtbar; optional -DMIXR_TX_LED_PULSE_US=… */
#endif

/** 0 = aus; sonst esp_timer_get_time ab dem LED wieder aus */
static int64_t s_tx_led_off_at_us = 0;

/* ---- TX-Aktivitäts-LED ------------------------------------------------------------------------ */

static void mixr_tx_led_set_level(int level_on)
{
#if MIXR_TX_LED_ACTIVE_LOW
    gpio_set_level(MIXR_PIN_TX_ACTIVITY, level_on ? 0 : 1);
#else
    gpio_set_level(MIXR_PIN_TX_ACTIVITY, level_on ? 1 : 0);
#endif
}

static void mixr_tx_led_init(void)
{
    gpio_config_t txled = {};
    txled.pin_bit_mask = 1ULL << MIXR_PIN_TX_ACTIVITY;
    txled.mode = GPIO_MODE_OUTPUT;
    txled.pull_up_en = GPIO_PULLUP_DISABLE;
    txled.pull_down_en = GPIO_PULLDOWN_DISABLE;
    txled.intr_type = GPIO_INTR_DISABLE;
    gpio_config(&txled);
    mixr_tx_led_set_level(0);
}

static void mixr_tx_led_pulse(void)
{
    mixr_tx_led_set_level(1);
    int64_t end = esp_timer_get_time() + (int64_t)MIXR_TX_LED_PULSE_US;
    if (end > s_tx_led_off_at_us) {
        s_tx_led_off_at_us = end;
    }
}

static void mixr_tx_led_tick(void)
{
    if (s_tx_led_off_at_us == 0) {
        return;
    }
    if (esp_timer_get_time() >= s_tx_led_off_at_us) {
        mixr_tx_led_set_level(0);
        s_tx_led_off_at_us = 0;
    }
}

/* ---- Senden an den PC (mit LED-Puls) ---------------------------------------------------------- */

static void send_to_pc(PktType type, const uint8_t *payload, uint8_t len)
{
    mixr_link_send(type, payload, len);
    mixr_tx_led_pulse();
}

void mixr_pc_send_media_cmd(uint8_t subcmd)
{
    if (!mixr_link_up()) {
        return;
    }
    /* Mit HID-Link: Medientaste direkt als Consumer-Control — funktioniert auch ohne die Windows-App. */
    uint16_t usage = MIXR_HID_USAGE_NONE;
    switch (static_cast<MediaSubCmd>(subcmd)) {
        case MediaSubCmd::NEXT:
            usage = MIXR_HID_USAGE_SCAN_NEXT;
            break;
        case MediaSubCmd::PLAY_PAUSE:
            usage = MIXR_HID_USAGE_PLAY_PAUSE;
            break;
        case MediaSubCmd::PREVIOUS:
            usage = MIXR_HID_USAGE_SCAN_PREV;
            break;
    }
    if (usage != MIXR_HID_USAGE_NONE && mixr_link_send_consumer(usage)) {
        mixr_tx_led_pulse();
        return;
    }
    send_to_pc(PktType::MEDIA_CMD, &subcmd, 1);
}

void mixr_pc_send_voip_mute(void)
{
    if (mixr_link_up()) {
        send_to_pc(PktType::VOIP_MUTE_CMD, nullptr, 0);
    }
}

void mixr_pc_send_voip_deafen(void)
{
    if (mixr_link_up()) {
        send_to_pc(PktType::VOIP_DEAFEN, nullptr, 0);
    }
}

void mixr_pc_send_share_screen(void)
{
    if (mixr_link_up()) {
        send_to_pc(PktType::SHARE_SCREEN_CMD, nullptr, 0);
    }
}

/* ---- Display / Touch / LVGL-Glue -------------------------------------------------------------- */

static void my_disp_flush(lv_display_t *disp, const lv_area_t *area, uint8_t *px_map)
{
    uint32_t w = (uint32_t)(area->x2 - area->x1 + 1);
    uint32_t h = (uint32_t)(area->y2 - area->y1 + 1);
    uint16_t *p = (uint16_t *)px_map;

#if MIXR_LCD_RGB565_SWAP_BYTES
    uint32_t cnt = w * h;
    for (uint32_t i = 0; i < cnt; i++) {
        p[i] = (uint16_t)((p[i] >> 8) | (p[i] << 8));
    }
#endif

    lcd_PushColors(area->x1, area->y1, w, h, p);
    lv_display_flush_ready(disp);
    /* Partial-Render: mehrere Streifen hintereinander ohne Yield → IDLE0 bekommt keine CPU,
     * Task-WDT (ESP-IDF) löst aus. Nach jedem Flush dem Idle-Task Zeit geben. */
    taskYIELD();
}

static void my_touchpad_read(lv_indev_t *indev, lv_indev_data_t *data)
{
    (void)indev;
    if (!mixr_touch_enabled()) {
        data->state = LV_INDEV_STATE_RELEASED;
        return;
    }
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

static void lv_tick_task(void *)
{
    lv_tick_inc(5);
}

static void encoder_timer_cb(void *)
{
    g_encoder.tick();
    mixr_tx_led_tick();
}

/* ---- Peripherie ------------------------------------------------------------------------------- */

static void init_hardware_peripherals(void)
{
    spi_bus_config_t buscfg = {};
    buscfg.mosi_io_num = MIXR_PIN_SPI_MOSI;
    buscfg.miso_io_num = MIXR_PIN_SPI_MISO;
    buscfg.sclk_io_num = MIXR_PIN_SPI_CLK;
    buscfg.quadwp_io_num = -1;
    buscfg.quadhd_io_num = -1;
    buscfg.max_transfer_sz = 32;
    ESP_ERROR_CHECK(spi_bus_initialize(MIXR_SPI_HOST, &buscfg, SPI_DMA_CH_AUTO));

    spi_device_interface_config_t devcfg = {};
    devcfg.mode = 0;
    devcfg.clock_speed_hz = 1 * 1000 * 1000;
    devcfg.spics_io_num = MIXR_PIN_SPI_CS;
    devcfg.queue_size = 1;
    ESP_ERROR_CHECK(spi_bus_add_device(MIXR_SPI_HOST, &devcfg, &spi_mcp));
    s_spi_mutex = xSemaphoreCreateMutex();

    gpio_config_t io_conf = {};
    io_conf.pin_bit_mask = MIXR_BUTTON_GPIO_MASK;
    io_conf.mode = GPIO_MODE_INPUT;
    io_conf.pull_up_en = GPIO_PULLUP_ENABLE;
    io_conf.pull_down_en = GPIO_PULLDOWN_DISABLE;
    io_conf.intr_type = GPIO_INTR_DISABLE;
    gpio_config(&io_conf);

    mixr_tx_led_init();

#if MIXR_HW_BUTTON3_DISABLED
    /* SW4 nicht auslesen: Pin reset, Eingangstreiber aus. */
    gpio_reset_pin(MIXR_PIN_BTN_3);
    gpio_input_disable(MIXR_PIN_BTN_3);
#endif

    g_encoder.init(MIXR_PIN_ENC_CLK, MIXR_PIN_ENC_DT, MIXR_PIN_ENC_SW);
}

static int mcp3008_read(int channel)
{
    uint8_t tx_data[3] = {
        0x01,
        (uint8_t)((0x08 + channel) << 4),
        0x00,
    };
    uint8_t rx_data[3] = {0};

    spi_transaction_t t = {};
    t.length = 24;
    t.tx_buffer = tx_data;
    t.rx_buffer = rx_data;

    if (s_spi_mutex != nullptr && xSemaphoreTake(s_spi_mutex, pdMS_TO_TICKS(20)) != pdTRUE) {
        return -1;
    }
    esp_err_t err = spi_device_transmit(spi_mcp, &t);
    if (s_spi_mutex != nullptr) {
        xSemaphoreGive(s_spi_mutex);
    }
    if (err != ESP_OK) {
        return -1;
    }
    return ((rx_data[1] & 0x03) << 8) | rx_data[2];
}

/* ---- Fader / Tasten --------------------------------------------------------------------------- */

static bool sliders_delta_over_deadband(const uint8_t *cur, const uint8_t *last)
{
    for (int j = 0; j < MIXR_SLIDER_COUNT; j++) {
        int d = (int)cur[j] - (int)last[j];
        if (d < 0) {
            d = -d;
        }
        if (d >= MIXR_SLIDER_DEADBAND) {
            return true;
        }
    }
    return false;
}

static void handle_button_press(int b)
{
    uint8_t btn_id = (uint8_t)b;
    /* Immer an den Host melden (Konfiguration, Log, Discord-Aktionen) … */
    send_to_pc(PktType::BTN_CMD, &btn_id, 1);
    /* … und Medientasten zusätzlich als HID-Consumer-Control, damit sie ohne App funktionieren.
     * Der Host weiß über HELLO (MIXR_CAP_HID_CONSUMER), dass er diese Tasten nicht doppelt ausführt. */
    uint16_t usage = mixr_proto_button_usage(b);
    if (usage != MIXR_HID_USAGE_NONE) {
        mixr_link_send_consumer(usage);
    }
}

static void mixr_poll_sliders_buttons(void)
{
    if (mixr_fw_update_active()) {
        return;
    }
    const bool link_up = mixr_link_up();

    if (link_up && mixr_sliders_send_enabled()) {
        uint8_t current_sliders[MIXR_SLIDER_COUNT];
        bool ok = true;
        for (int j = 0; j < MIXR_SLIDER_COUNT; j++) {
            int v = mcp3008_read(j);
            if (v < 0) {
                ok = false;
                break;
            }
            current_sliders[j] = (uint8_t)(v >> 2);
        }
        if (ok && sliders_delta_over_deadband(current_sliders, s_last_sliders)) {
            send_to_pc(PktType::SLIDER_VALS, current_sliders, MIXR_SLIDER_COUNT);
            memcpy(s_last_sliders, current_sliders, MIXR_SLIDER_COUNT);
        }
    }

    for (int b = 0; b < MIXR_BUTTON_COUNT; b++) {
        uint8_t state;
#if MIXR_HW_BUTTON3_DISABLED
        if (b == 3) {
            state = 1;
        } else
#endif
        {
            state = gpio_get_level(mixr_button_gpios[b]);
        }
        if (mixr_buttons_send_enabled() && state == 0 && s_last_buttons[b] == 1) {
            int64_t now_us = esp_timer_get_time();
            if (now_us - s_last_btn_cmd_us[b] >= MIXR_BUTTON_DEBOUNCE_US) {
                s_last_btn_cmd_us[b] = now_us;
                if (link_up) {
                    handle_button_press(b);
                } else {
                    /* Ohne PC-Verbindung bleiben die HID-Medientasten trotzdem nutzbar (Standard-Map). */
                    uint16_t usage = mixr_proto_button_usage(b);
                    if (usage != MIXR_HID_USAGE_NONE) {
                        mixr_link_send_consumer(usage);
                    }
                }
            }
        }
        s_last_buttons[b] = state;
    }
}

extern "C" void mixr_sliders_resync_baseline(void)
{
    for (int j = 0; j < MIXR_SLIDER_COUNT; j++) {
        int v = mcp3008_read(j);
        if (v >= 0) {
            s_last_sliders[j] = (uint8_t)(v >> 2);
        }
    }
    if (mixr_sliders_send_enabled() && mixr_link_up()) {
        send_to_pc(PktType::SLIDER_VALS, s_last_sliders, MIXR_SLIDER_COUNT);
    }
}

static void mixr_controls_timer_cb(void *)
{
    mixr_poll_sliders_buttons();
}

/* ---- Hauptprogramm ---------------------------------------------------------------------------- */

void mixr_app_run(void)
{
    s_mixr_boot_count++;
    ESP_LOGI(TAG, "Start #%lu, Firmware %s", (unsigned long)s_mixr_boot_count, esp_app_get_description()->version);
    mixr_fw_update_mark_valid();

    rm67162_init();
    lcd_setRotation(0);
    touch.begin();
    init_hardware_peripherals();

    lv_init();
    /* Halber Frame (~128 KiB) passt oft nicht ins interne DRAM → Ausweich nach PSRAM.
     * PSRAM + SW-Renderer beim Cover = sehr langsam → IDLE0-WDT / Neustart.
     * ~1/8 Frame (~32 KiB) bleibt i. d. R. intern und schnell genug (evtl. etwas mehr Partial-Streifen). */
    const size_t total_px = (size_t)TFT_WIDTH * (size_t)TFT_HEIGHT;
    const size_t buf_pixels = total_px / 8U;
    const size_t buf_size = buf_pixels;

    void *disp_buf = heap_caps_malloc(buf_size * sizeof(lv_color_t), MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
    if (disp_buf == nullptr) {
        ESP_LOGW(TAG, "Interner RAM voll, nutze PSRAM fuer Display Buffer");
        disp_buf = heap_caps_malloc(buf_size * sizeof(lv_color_t), MALLOC_CAP_SPIRAM);
    }
    if (disp_buf == nullptr) {
        ESP_LOGE(TAG, "LVGL Display Buffer: Allokation fehlgeschlagen");
        abort();
    }

    lv_display_t *disp = lv_display_create(TFT_WIDTH, TFT_HEIGHT);
    lv_display_set_buffers(disp, disp_buf, nullptr, buf_size * sizeof(lv_color_t), LV_DISPLAY_RENDER_MODE_PARTIAL);
    lv_display_set_flush_cb(disp, my_disp_flush);
    lv_display_set_default(disp);
#if LV_USE_THEME_DEFAULT
    /* Dunkles Theme: Partial-Render löscht mit passender Hintergrundfarbe (nicht Weiß). */
    lv_theme_t *theme = lv_theme_default_init(disp, lv_palette_main(LV_PALETTE_BLUE), lv_palette_main(LV_PALETTE_GREY),
                                              true, MIXR_UI_FONT);
    lv_display_set_theme(disp, theme);
#endif
    {
        lv_obj_t *ds = lv_display_get_screen_active(disp);
        if (ds != nullptr) {
            /* Gleicher Rostton wie Carousel (MIXR_COLOR_BG), sonst Streifen am Rand */
            lv_obj_set_style_bg_color(ds, lv_color_hex(0x765858), 0);
            lv_obj_set_style_bg_opa(ds, LV_OPA_COVER, 0);
            lv_obj_set_style_border_width(ds, 0, 0);
            lv_obj_set_style_outline_width(ds, 0, 0);
        }
    }

    lv_indev_t *indev = lv_indev_create();
    lv_indev_set_type(indev, LV_INDEV_TYPE_POINTER);
    lv_indev_set_read_cb(indev, my_touchpad_read);

    img_buf = (uint8_t *)heap_caps_malloc(MIXR_COVER_RGB565_BYTES, MALLOC_CAP_SPIRAM);
    if (img_buf == nullptr) {
        ESP_LOGE(TAG, "PSRAM Allokation fuer Cover fehlgeschlagen");
    }

    mixr_ui_set_last_reset_reason(static_cast<int>(esp_reset_reason()));
    mixr_ui_init(disp, img_buf, MIXR_COVER_RGB565_BYTES, s_mixr_boot_count);
    if (img_buf == nullptr) {
        mixr_ui_set_error_banner("Cover: PSRAM fehlt");
    }

    const esp_timer_create_args_t timer_args = {
        .callback = &lv_tick_task,
        .arg = nullptr,
        .dispatch_method = ESP_TIMER_TASK,
        .name = "tick",
        .skip_unhandled_events = false,
    };
    esp_timer_handle_t timer;
    ESP_ERROR_CHECK(esp_timer_create(&timer_args, &timer));
    ESP_ERROR_CHECK(esp_timer_start_periodic(timer, 5000));

    const esp_timer_create_args_t enc_timer_args = {
        .callback = &encoder_timer_cb,
        .arg = nullptr,
        .dispatch_method = ESP_TIMER_TASK,
        .name = "enc_poll",
        .skip_unhandled_events = false,
    };
    esp_timer_handle_t enc_timer;
    ESP_ERROR_CHECK(esp_timer_create(&enc_timer_args, &enc_timer));
    /* 1 ms: Quadratur zwischen zwei langsamen Hauptschleifen-Takten nicht verlieren */
    ESP_ERROR_CHECK(esp_timer_start_periodic(enc_timer, 1000));

    const esp_timer_create_args_t controls_timer_args = {
        .callback = &mixr_controls_timer_cb,
        .arg = nullptr,
        .dispatch_method = ESP_TIMER_TASK,
        .name = "usb_ctrl",
        .skip_unhandled_events = false,
    };
    esp_timer_handle_t controls_timer;
    ESP_ERROR_CHECK(esp_timer_create(&controls_timer_args, &controls_timer));
    /* ~100 Hz: direkteres Slider-Feeling am Host bei weiterhin moderater Last */
    ESP_ERROR_CHECK(esp_timer_start_periodic(controls_timer, 10000));

    ui_queue = xQueueCreate(24, sizeof(UiMessage));

    /* Kommunikation: Frame-Handler + Link-Backend (startet comm_task) */
    mixr_proto_init(ui_queue, img_buf);
    mixr_log_stream_init();
    mixr_link_init(mixr_proto_handle_frame);
    ESP_LOGI(TAG, "Link: %s, Protokoll v%d", mixr_link_name(), MIXR_PROTOCOL_VERSION);

    bool last_usb_state = mixr_link_up();
    mixr_ui_set_usb_connected(last_usb_state);
    if (last_usb_state) {
        mixr_proto_send_hello();
    }

    UiMessage incoming_msg;
    uint32_t last_dbg_ms = 0;

    while (true) {
        uint32_t ms = esp_log_timestamp();
        if (ms - last_dbg_ms >= 1000) {
            last_dbg_ms = ms;
            mixr_ui_set_debug_overlay(s_mixr_boot_count, ms / 1000);
            mixr_ui_on_focus_timer_tick();
        }

        bool current_usb_state = mixr_link_up();
        if (current_usb_state != last_usb_state) {
            mixr_ui_set_usb_connected(current_usb_state);
            last_usb_state = current_usb_state;
            if (current_usb_state) {
                mixr_proto_send_hello();
            } else {
                mixr_proto_on_link_down();
                if (mixr_ui_is_menu_open()) {
                    mixr_ui_menu_refresh_dynamic_rows();
                }
            }
        }

        if (g_encoder.consume_long_press()) {
            mixr_ui_goto_first_slide();
        } else if (!mixr_ui_is_menu_open()) {
            int8_t step = g_encoder.read_detent_step();
            bool click = g_encoder.consume_click();
            if (step != 0 || click) {
                mixr_ui_main_navigate(step, click);
            }
        } else {
            int8_t step = g_encoder.read_detent_step();
            bool click = g_encoder.consume_click();
            mixr_ui_menu_navigate(step, click);
        }

        /* Alle anstehenden UI-Nachrichten in einem Durchlauf abarbeiten (nicht nur eine pro 10 ms). */
        while (xQueueReceive(ui_queue, &incoming_msg, 0) == pdTRUE) {
            mixr_ui_on_message(&incoming_msg);
        }

        lv_timer_handler();
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}
