/*
 * Legacy-Backend: USB-Serial/JTAG mit 0xAA-Framing.
 *
 * Dieselbe Schnittstelle nutzt idf.py monitor (Logs) und das Binärprotokoll — rohe Protokollbytes
 * erscheinen im Terminal als „Krautzeichen“. Für Produktgeräte ist das HID-Backend (mixr_link_usb.cpp)
 * vorgesehen; dieses Backend bleibt für Bench-Debugging und Boards ohne HID-Firmware.
 */
#include "sdkconfig.h"
#if !CONFIG_MIXR_USB_HID

#include "mixr_link.hpp"

#include "driver/usb_serial_jtag.h"
#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/semphr.h"
#include "freertos/task.h"

#include <cstring>

static const char *TAG = "mixr_link";

static mixr_frame_handler_t s_handler = nullptr;
static SemaphoreHandle_t s_tx_mutex = nullptr;

static void comm_task(void *)
{
    uint8_t rx_buf[256];
    uint8_t state = 0;
    uint8_t len = 0;
    uint8_t type = 0;
    uint8_t payload[256];
    uint8_t payload_idx = 0;
    uint8_t crc = 0;

    while (true) {
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
                    if (rx_byte == crc && s_handler != nullptr) {
                        s_handler(static_cast<PktType>(type), payload, len);
                    }
                    state = 0;
                    break;
            }
        }
    }
}

void mixr_link_init(mixr_frame_handler_t handler)
{
    s_handler = handler;
    s_tx_mutex = xSemaphoreCreateMutex();

    /* Großer RX-Puffer gegen Überlauf bei Cover-Bursts (Host sendet 115 KB ohne Flusskontrolle). */
    usb_serial_jtag_driver_config_t usb_config = {
        .tx_buffer_size = 512,
        .rx_buffer_size = 65536,
    };
    ESP_ERROR_CHECK(usb_serial_jtag_driver_install(&usb_config));

    /* USB kurz stabilisieren (Enumeration), bevor der große RX-Strom kommt */
    vTaskDelay(pdMS_TO_TICKS(500));
    /* 8 KiB: esp_ota_write + SHA-256 + JPEG-Decode laufen im Handler. */
    xTaskCreate(comm_task, "comm_task", 8192, nullptr, 5, nullptr);
    ESP_LOGI(TAG, "Link: USB-Serial/JTAG (Legacy-Framing)");
}

bool mixr_link_up(void)
{
    return usb_serial_jtag_is_connected();
}

void mixr_link_send(PktType type, const uint8_t *payload, uint8_t len)
{
    uint8_t packet[4 + MIXR_PAYLOAD_MAX];
    packet[0] = PKT_START_BYTE;
    packet[1] = len;
    packet[2] = static_cast<uint8_t>(type);

    uint8_t crc = len ^ static_cast<uint8_t>(type);
    for (uint8_t i = 0; i < len; i++) {
        packet[3 + i] = payload[i];
        crc ^= payload[i];
    }
    packet[3 + len] = crc;

    if (s_tx_mutex != nullptr && xSemaphoreTake(s_tx_mutex, pdMS_TO_TICKS(200)) != pdTRUE) {
        return;
    }
    usb_serial_jtag_write_bytes(packet, 4U + (size_t)len, pdMS_TO_TICKS(500));
    if (s_tx_mutex != nullptr) {
        xSemaphoreGive(s_tx_mutex);
    }
}

uint8_t mixr_link_caps(void)
{
    return MIXR_CAP_JPEG_COVER | MIXR_CAP_BOOTLOADER_CMD | MIXR_CAP_LOG_STREAM;
}

bool mixr_link_send_consumer(uint16_t)
{
    return false;
}

const char *mixr_link_name(void)
{
    return "USB-Serial/JTAG";
}

void mixr_link_prepare_bootloader(void)
{
    /* Serial/JTAG: nichts zu tun — Port bleibt für esptool nutzbar. */
}

#endif /* !CONFIG_MIXR_USB_HID */
