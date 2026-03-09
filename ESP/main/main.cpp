#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "esp_timer.h"
#include "esp_log.h"
#include "esp_heap_caps.h"
#include "driver/usb_serial_jtag.h"
#include "driver/spi_master.h"
#include "driver/gpio.h"
#include "lvgl.h"
#include "rm67162.h"
#include "FT3168.h"
#include "protocol.h"
#include <string.h>

static const char *TAG = "MIX_MAIN";

// --- HARDWARE PINS & MASKEN ---
#define PIN_NUM_MISO 12
#define PIN_NUM_MOSI 11
#define PIN_NUM_CLK  13
#define PIN_NUM_CS   14

// Neue Button Pins: 2, 26, 15, 10, 4
#define BUTTON_MASK ((1ULL<<2) | (1ULL<<21) | (1ULL<<15) | (1ULL<<10) | (1ULL<<4))

FT3168 touch(I2C_SDA, I2C_SCL, -1, -1);
spi_device_handle_t spi_mcp;

// --- LVGL OBJEKTE & SPEICHER ---
lv_obj_t *screen_loading;
lv_obj_t *screen_player;
lv_obj_t *ui_title;
lv_obj_t *ui_artist;
lv_obj_t *ui_cover;

uint8_t *img_buf = nullptr;
uint32_t img_offset = 0;
const uint32_t IMG_SIZE = 240 * 240 * 2; // 115.200 Bytes
lv_image_dsc_t img_dsc = {0};

QueueHandle_t ui_queue;

// --- LVGL CALLBACKS ---
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

// --- UI SETUP ---
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

    ui_cover = lv_image_create(screen_player);
    lv_obj_set_size(ui_cover, 240, 240);
    lv_obj_align(ui_cover, LV_ALIGN_TOP_MID, 0, 10);

    ui_title = lv_label_create(screen_player);
    lv_label_set_text(ui_title, "Aktueller Song");
    lv_obj_set_style_text_font(ui_title, &lv_font_montserrat_14, 0); // Schriftgröße 14 ist Standard
    lv_obj_set_style_text_color(ui_title, lv_color_white(), 0);
    lv_obj_align(ui_title, LV_ALIGN_CENTER, 0, 100);

    ui_artist = lv_label_create(screen_player);
    lv_label_set_text(ui_artist, "Kuenstler Name");
    lv_obj_set_style_text_color(ui_artist, lv_palette_lighten(LV_PALETTE_GREY, 1), 0);
    lv_obj_align(ui_artist, LV_ALIGN_CENTER, 0, 130);
}

// --- HARDWARE FUNKTIONEN ---
void init_hardware_peripherals() {
    // 1. SPI Bus für MCP3008 (SPI3_HOST statt SPI2_HOST)
    spi_bus_config_t buscfg = {};
    buscfg.mosi_io_num = PIN_NUM_MOSI;
    buscfg.miso_io_num = PIN_NUM_MISO;
    buscfg.sclk_io_num = PIN_NUM_CLK;
    buscfg.quadwp_io_num = -1;
    buscfg.quadhd_io_num = -1;
    buscfg.max_transfer_sz = 32;
    spi_bus_initialize(SPI3_HOST, &buscfg, SPI_DMA_CH_AUTO);

    spi_device_interface_config_t devcfg = {};
    devcfg.mode = 0;
    devcfg.clock_speed_hz = 1 * 1000 * 1000; // 1 MHz
    devcfg.spics_io_num = PIN_NUM_CS;
    devcfg.queue_size = 1;
    spi_bus_add_device(SPI3_HOST, &devcfg, &spi_mcp);

    // 2. GPIO Buttons (Active Low, Pull-Up)
    gpio_config_t io_conf = {};
    io_conf.pin_bit_mask = BUTTON_MASK;
    io_conf.mode = GPIO_MODE_INPUT;
    io_conf.pull_up_en = GPIO_PULLUP_ENABLE;
    io_conf.pull_down_en = GPIO_PULLDOWN_DISABLE;
    io_conf.intr_type = GPIO_INTR_DISABLE;
    gpio_config(&io_conf);
}

int mcp3008_read(int channel) {
    uint8_t tx_data[3] = {
        0x01,                             // Start Bit
        (uint8_t)((0x08 + channel) << 4), // Single-ended Modus + Kanal
        0x00
    };
    uint8_t rx_data[3] = {0};

    spi_transaction_t t = {};
    t.length = 24;
    t.tx_buffer = tx_data;
    t.rx_buffer = rx_data;

    if (spi_device_transmit(spi_mcp, &t) != ESP_OK) return 0;
    return ((rx_data[1] & 0x03) << 8) | rx_data[2];
}

// --- KOMMUNIKATION ---
void send_to_pc(PktType type, const uint8_t *payload, uint8_t len) {
    if (len > 250) return;

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
    


    // HIER WIEDER AKTIVIEREN
    //usb_serial_jtag_write_bytes(packet, 4 + len, portMAX_DELAY);
}

void comm_task(void *pvParameters) {
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
                    if (rx_byte == PKT_START_BYTE) { state = 1; crc = 0; }
                    break;
                case 1: 
                    len = rx_byte; crc ^= rx_byte; state = 2;
                    break;
                case 2: 
                    type = rx_byte; crc ^= rx_byte; payload_idx = 0; state = (len > 0) ? 3 : 4;
                    break;
                case 3: 
                    payload[payload_idx++] = rx_byte; crc ^= rx_byte;
                    if (payload_idx == len) state = 4;
                    break;
                case 4: 
                    if (rx_byte == crc) {
                        UiMessage msg;
                        msg.type = static_cast<PktType>(type);
                        
                        if (msg.type == PktType::IMAGE_CHUNK) {
                            if (img_buf != nullptr) {
                                uint32_t copy_len = len;
                                if (img_offset + copy_len > IMG_SIZE) copy_len = IMG_SIZE - img_offset; 
                                
                                memcpy(img_buf + img_offset, payload, copy_len);
                                img_offset += copy_len;

                                if (img_offset >= IMG_SIZE) {
                                    msg.type = PktType::IMAGE_READY;
                                    xQueueSend(ui_queue, &msg, 0);
                                    img_offset = 0;
                                }
                            }
                        } else {
                            if (msg.type == PktType::SONG_TITLE || msg.type == PktType::SONG_ARTIST) {
                                uint8_t copy_len = (len < sizeof(msg.payload.text) - 1) ? len : sizeof(msg.payload.text) - 1;
                                memcpy(msg.payload.text, payload, copy_len);
                                msg.payload.text[copy_len] = '\0';
                            }
                            xQueueSend(ui_queue, &msg, 0);
                        }
                    }
                    state = 0;
                    break;
            }
        }
    }
}

// --- MAIN ---
extern "C" void app_main(void)
{
    // 1. Core Hardware Init
    rm67162_init();
    
    usb_serial_jtag_driver_config_t usb_config = {
        .tx_buffer_size = 256,
        .rx_buffer_size = 8192
    };
    usb_serial_jtag_driver_install(&usb_config);

    lcd_setRotation(0);
    touch.begin();
    init_hardware_peripherals();

    // 2. LVGL Init & Speicherallokation
    lv_init();
    size_t buf_size = (TFT_WIDTH * TFT_HEIGHT) / 10;
    
    // Versuch 1: Interner RAM für Performance und Watchdog-Prävention
    void *buf = heap_caps_malloc(buf_size * sizeof(lv_color_t), MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
    if (buf == NULL) {
        ESP_LOGW(TAG, "Interner RAM voll, nutze PSRAM fuer Display Buffer");
        buf = heap_caps_malloc(buf_size * sizeof(lv_color_t), MALLOC_CAP_SPIRAM);
    }
    
    lv_display_t *disp = lv_display_create(TFT_WIDTH, TFT_HEIGHT);
    lv_display_set_buffers(disp, buf, NULL, buf_size * sizeof(lv_color_t), LV_DISPLAY_RENDER_MODE_PARTIAL);
    lv_display_set_flush_cb(disp, my_disp_flush);

    lv_indev_t *indev = lv_indev_create();
    lv_indev_set_type(indev, LV_INDEV_TYPE_POINTER);
    lv_indev_set_read_cb(indev, my_touchpad_read);

    // 3. PSRAM für das 115KB Bild
    img_buf = (uint8_t*)heap_caps_malloc(IMG_SIZE, MALLOC_CAP_SPIRAM);
    if (img_buf == NULL) ESP_LOGE(TAG, "Kritischer Fehler: PSRAM Allokation fehlgeschlagen");

    // 4. Tasks & Timer
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

    ui_queue = xQueueCreate(10, sizeof(UiMessage));
    xTaskCreate(comm_task, "comm_task", 4096, NULL, 5, NULL);

    // 5. UI Setup
    create_loading_screen();
    create_player_screen();
    lv_screen_load(screen_loading);

    // 6. Main Loop & Variablen (außerhalb der Schleife deklariert)
    // 6. Main Loop & Variablen (außerhalb der Schleife deklariert)
    bool last_usb_state = false;
    UiMessage incoming_msg;
    
    uint8_t last_sliders[8] = {0};
    uint8_t last_buttons[5] = {1, 1, 1, 1, 1}; 
    const int btn_pins[5] = {2, 21, 15, 10, 4};

    while (1) {
        bool current_usb_state = usb_serial_jtag_is_connected();
        
        if (current_usb_state != last_usb_state) {
            lv_screen_load(current_usb_state ? screen_player : screen_loading);
            last_usb_state = current_usb_state;
        }

        if (current_usb_state) {
            // A. Slider lesen (8 Kanäle)
            uint8_t current_sliders[8];
            for(int j=0; j<8; j++) {
                current_sliders[j] = (uint8_t)(mcp3008_read(j) >> 2); // 10-bit zu 8-bit Skalierung
            }
            static uint32_t last_log_time = 0;
            if (esp_log_timestamp() - last_log_time > 1000) {
                ESP_LOGI(TAG, "ADC Werte: 0:%3d | 1:%3d | 2:%3d | 3:%3d | 4:%3d | 5:%3d | 6:%3d | 7:%3d",
                         current_sliders[0], current_sliders[1], current_sliders[2], current_sliders[3],
                         current_sliders[4], current_sliders[5], current_sliders[6], current_sliders[7]);
                last_log_time = esp_log_timestamp();
            }
            if (memcmp(current_sliders, last_sliders, 8) != 0) {
                send_to_pc(PktType::SLIDER_VALS, current_sliders, 8);
                memcpy(last_sliders, current_sliders, 8);
            }


            // B. Buttons lesen (Falling Edge Detection)
            for(int b=0; b<5; b++) {
                uint8_t state = gpio_get_level((gpio_num_t)btn_pins[b]);
                if(state == 0 && last_buttons[b] == 1) { 
                    uint8_t btn_id = (uint8_t)b;
                    send_to_pc(PktType::BTN_CMD, &btn_id, 1);
                }
                last_buttons[b] = state;
            }
        }

        // C. UI Updates
        if (xQueueReceive(ui_queue, &incoming_msg, 0) == pdTRUE) {
            switch (incoming_msg.type) {
                case PktType::SONG_TITLE:
                    if (ui_title) lv_label_set_text(ui_title, incoming_msg.payload.text);
                    break;

                case PktType::SONG_ARTIST:
                    if (ui_artist) lv_label_set_text(ui_artist, incoming_msg.payload.text);
                    break;

                case PktType::IMAGE_READY:
                    if (ui_cover && img_buf) {
                        img_dsc.header.magic = LV_IMAGE_HEADER_MAGIC;
                        img_dsc.header.cf = LV_COLOR_FORMAT_RGB565;
                        img_dsc.header.w = 240;
                        img_dsc.header.h = 240;
                        img_dsc.header.stride = 240 * 2;
                        img_dsc.data_size = IMG_SIZE;
                        img_dsc.data = img_buf;
                        lv_image_set_src(ui_cover, &img_dsc);
                    }
                    break;

                default:
                    break;
            }
        }

        lv_timer_handler();
        vTaskDelay(pdMS_TO_TICKS(30)); // 30ms = ~33Hz Polling-Rate
    }
    
}