/*
 * USB-Serial/JTAG: Dieselbe Schnittstelle nutzt idf.py monitor (Logs) und das
 * Mixr-Binärprotokoll (send_to_pc / comm_task). Sobald Pakete gesendet werden
 * (z. B. Slider geändert, Taste), erscheinen rohe Bytes im Terminal als
 * „Krautzeichen“ — das ist keine zufällige Störung, sondern Daten + Text im
 * selben Strom. Produkt: ein USB-Kabel (native USB-Serial/JTAG) für Logs + Protokoll.
 */

#include "board_pins.h"
#include "encoder_ky040.hpp"
#include "protocol.h"
#include "rm67162.h"
#include "ui_mixr.hpp"
#include "mixr_settings.hpp"

#include "FT3168.h"
#include "driver/gpio.h"
#include "driver/spi_master.h"
#include "driver/usb_serial_jtag.h"
#include "esp_attr.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "lvgl.h"
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

static uint8_t *img_buf = nullptr;
static uint32_t img_offset = 0;
static const uint32_t IMG_SIZE = 240 * 240 * 2;

static QueueHandle_t ui_queue;
static Ky040Encoder g_encoder;

/** USB-Slider/Buttons: vom Timer statt aus main, damit es bei langem LVGL-Cover-Draw weiterläuft */
static uint8_t s_last_sliders[MIXR_SLIDER_COUNT] = {0};
static uint8_t s_last_buttons[5] = {1, 1, 1, 1, 1};

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
    if (!mixr_touch_enabled()) {
        data->state = LV_INDEV_STATE_RELEASED;
        (void)indev;
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
    (void)indev;
}

static void lv_tick_task(void *arg)
{
    (void)arg;
    lv_tick_inc(5);
}

static void encoder_timer_cb(void *arg)
{
    (void)arg;
    g_encoder.tick();
}

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

    gpio_config_t io_conf = {};
    io_conf.pin_bit_mask = MIXR_BUTTON_GPIO_MASK;
    io_conf.mode = GPIO_MODE_INPUT;
    io_conf.pull_up_en = GPIO_PULLUP_ENABLE;
    io_conf.pull_down_en = GPIO_PULLDOWN_DISABLE;
    io_conf.intr_type = GPIO_INTR_DISABLE;
    gpio_config(&io_conf);

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

    if (spi_device_transmit(spi_mcp, &t) != ESP_OK) {
        return 0;
    }
    return ((rx_data[1] & 0x03) << 8) | rx_data[2];
}

static bool mixr_pc_link_up(void)
{
    return usb_serial_jtag_is_connected();
}

static void send_to_pc(PktType type, const uint8_t *payload, uint8_t len)
{
    uint8_t packet[256];
    packet[0] = PKT_START_BYTE;
    packet[1] = len;
    packet[2] = static_cast<uint8_t>(type);

    uint8_t crc = len ^ static_cast<uint8_t>(type);
    for (uint8_t i = 0; i < len; i++) {
        packet[3 + i] = payload[i];
        crc ^= payload[i];
    }
    packet[3 + len] = crc;

    usb_serial_jtag_write_bytes(packet, 4 + len, portMAX_DELAY);
}

void mixr_pc_send_media_cmd(uint8_t subcmd)
{
    if (!mixr_pc_link_up()) {
        return;
    }
    send_to_pc(PktType::MEDIA_CMD, &subcmd, 1);
}

static void comm_task(void *pvParameters)
{
    (void)pvParameters;
    uint8_t rx_buf[256];
    uint8_t state = 0;
    uint8_t len = 0;
    uint8_t type = 0;
    uint8_t payload[256];
    uint8_t payload_idx = 0;
    uint8_t crc = 0;

    while (1) {
        int bytes_read = usb_serial_jtag_read_bytes(rx_buf, sizeof(rx_buf), portMAX_DELAY);

        for (int i = 0; i < bytes_read; i++) {
            uint8_t rx_byte = rx_buf[i];

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
                    if (payload_idx == len) {
                        state = 4;
                    }
                    break;
                case 4:
                    if (rx_byte == crc) {
                        UiMessage msg;
                        msg.type = static_cast<PktType>(type);

                        if (msg.type == PktType::IMAGE_CHUNK) {
                            if (img_buf != nullptr) {
                                uint32_t copy_len = len;
                                if (img_offset + copy_len > IMG_SIZE) {
                                    copy_len = IMG_SIZE - img_offset;
                                }

                                memcpy(img_buf + img_offset, payload, copy_len);
                                img_offset += copy_len;

                                if (img_offset >= IMG_SIZE) {
                                    msg.type = PktType::IMAGE_READY;
                                    xQueueSend(ui_queue, &msg, 0);
                                    img_offset = 0;
                                }
                            }
                        } else if (msg.type == PktType::SONG_TITLE || msg.type == PktType::SONG_ARTIST) {
                            /* Neuer Titel/Artist: altes halbes Cover verwerfen */
                            img_offset = 0;
                            uint8_t copy_len =
                                (len < sizeof(msg.payload.text) - 1) ? len : sizeof(msg.payload.text) - 1;
                            memcpy(msg.payload.text, payload, copy_len);
                            msg.payload.text[copy_len] = '\0';
                            xQueueSend(ui_queue, &msg, 0);
                        }
                        /* andere Typen vom PC ignorieren (keine halb initialisierte UiMessage in die Queue) */
                    } else {
                        /* CRC falsch: Frame verworfen, Cover-Reassembly nicht mehr vertrauen */
                        img_offset = 0;
                    }
                    state = 0;
                    break;
            }
        }
    }
}

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

static void mixr_poll_sliders_buttons(void)
{
    if (!mixr_pc_link_up()) {
        return;
    }
    if (mixr_sliders_send_enabled()) {
        uint8_t current_sliders[MIXR_SLIDER_COUNT];
        for (int j = 0; j < MIXR_SLIDER_COUNT; j++) {
            current_sliders[j] = (uint8_t)(mcp3008_read(j) >> 2);
        }
        if (sliders_delta_over_deadband(current_sliders, s_last_sliders)) {
            send_to_pc(PktType::SLIDER_VALS, current_sliders, MIXR_SLIDER_COUNT);
            memcpy(s_last_sliders, current_sliders, MIXR_SLIDER_COUNT);
        }
    }
    for (int b = 0; b < 5; b++) {
        uint8_t state = gpio_get_level(mixr_button_gpios[b]);
        if (mixr_buttons_send_enabled() && state == 0 && s_last_buttons[b] == 1) {
            uint8_t btn_id = (uint8_t)b;
            send_to_pc(PktType::BTN_CMD, &btn_id, 1);
        }
        s_last_buttons[b] = state;
    }
}

extern "C" void mixr_sliders_resync_baseline(void)
{
    for (int j = 0; j < MIXR_SLIDER_COUNT; j++) {
        s_last_sliders[j] = (uint8_t)(mcp3008_read(j) >> 2);
    }
    if (mixr_sliders_send_enabled() && mixr_pc_link_up()) {
        send_to_pc(PktType::SLIDER_VALS, s_last_sliders, MIXR_SLIDER_COUNT);
    }
}

static void mixr_controls_timer_cb(void *arg)
{
    (void)arg;
    mixr_poll_sliders_buttons();
}

void mixr_app_run(void)
{
    s_mixr_boot_count++;
    ESP_LOGI(TAG, "Start #%lu", (unsigned long)s_mixr_boot_count);

    rm67162_init();

    /* Ein Kabel: USB-Serial/JTAG für Protokoll + Logs. Großer RX-Puffer gegen Überlauf bei Cover-Bursts. */
    usb_serial_jtag_driver_config_t usb_config = {
        .tx_buffer_size = 512,
        .rx_buffer_size = 65536,
    };
    ESP_ERROR_CHECK(usb_serial_jtag_driver_install(&usb_config));

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

    void *disp_buf =
        heap_caps_malloc(buf_size * sizeof(lv_color_t), MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
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
                                              true, &lv_font_montserrat_22);
    lv_display_set_theme(disp, theme);
#endif
    /* Default-Screen-BG = gleiche RGB565 wie screen_player (0x0e0e12), sonst minimaler Farbversatz
     * zwischen Theme-Fill und UI → dünner Streifen an Kachelrändern (früher oft kein LVGL-Partial). */
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

    img_buf = (uint8_t *)heap_caps_malloc(IMG_SIZE, MALLOC_CAP_SPIRAM);
    if (img_buf == nullptr) {
        ESP_LOGE(TAG, "PSRAM Allokation fuer Cover fehlgeschlagen");
    }

    mixr_ui_set_last_reset_reason(static_cast<int>(esp_reset_reason()));
    mixr_ui_init(disp, img_buf, IMG_SIZE, s_mixr_boot_count);
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
    /* ~40 Hz: genug für „Echtzeit“ am Host, entlastet main während LVGL-Redraw */
    ESP_ERROR_CHECK(esp_timer_start_periodic(controls_timer, 25000));

    ui_queue = xQueueCreate(24, sizeof(UiMessage));
    /* USB kurz stabilisieren (Enumeration), bevor große RX-Strom kommt */
    vTaskDelay(pdMS_TO_TICKS(500));
    xTaskCreate(comm_task, "comm_task", 4096, nullptr, 5, nullptr);

    mixr_ui_set_usb_connected(mixr_pc_link_up());

    bool last_usb_state = usb_serial_jtag_is_connected();
    UiMessage incoming_msg;

    uint32_t last_dbg_ms = 0;

    while (1) {
        uint32_t ms = esp_log_timestamp();
        if (ms - last_dbg_ms >= 1000) {
            last_dbg_ms = ms;
            mixr_ui_set_debug_overlay(s_mixr_boot_count, ms / 1000);
            mixr_ui_on_focus_timer_tick();
        }
        bool current_usb_state = usb_serial_jtag_is_connected();
        if (current_usb_state != last_usb_state) {
            mixr_ui_set_usb_connected(current_usb_state);
            last_usb_state = current_usb_state;
            if (!current_usb_state && mixr_ui_is_menu_open()) {
                mixr_ui_menu_refresh_dynamic_rows();
            }
        }

        if (!mixr_ui_is_menu_open()) {
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

        if (xQueueReceive(ui_queue, &incoming_msg, 0) == pdTRUE) {
            mixr_ui_on_message(&incoming_msg);
        }

        lv_timer_handler();
        vTaskDelay(pdMS_TO_TICKS(10));
    }
}
