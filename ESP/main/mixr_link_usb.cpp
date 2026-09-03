/*
 * USB-HID-Backend (TinyUSB, USB-OTG-Peripheral des ESP32-S3).
 *
 * Composite-Gerät:
 *   ITF 0  Vendor-HID (Usage Page 0xFF00), 64-Byte IN/OUT-Reports → Mixr-Protokoll (treiberlos auf allen OS)
 *   ITF 1  HID Consumer Control → Play/Pause/Next/Prev/Mute funktionieren auch ohne die Windows-App
 *   ITF 2+ CDC-ACM (nur CONFIG_MIXR_USB_DEBUG_CDC) → Konsole/Logs für Entwickler
 *
 * Hinweis: USB-OTG und USB-Serial/JTAG teilen sich die USB-Pins — in diesem Modus gibt es kein
 * JTAG-Debugging über USB und kein `idf.py monitor` (außer über das Debug-CDC). Der ROM-Bootloader
 * (Download-Modus) bleibt erreichbar: ENTER_BOOTLOADER startet dorthin, das Gerät erscheint dann als
 * Espressif-COM-Port (303A:1001) und die App flasht mit esptool.
 */
#include "sdkconfig.h"
#if CONFIG_MIXR_USB_HID

#include "mixr_link.hpp"

#include "esp_log.h"
#include "esp_mac.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "freertos/task.h"
#include "tinyusb.h"
#include "tinyusb_default_config.h"
#include "tusb.h"
#if CONFIG_MIXR_USB_DEBUG_CDC
#include "tinyusb_cdc_acm.h"
#include "tinyusb_console.h"
#endif

#include <cstdio>
#include <cstring>

static const char *TAG = "mixr_usb";

/* ---- Interfaces / Endpoints ------------------------------------------------------------------ */

enum {
    ITF_HID_VENDOR = 0,
    ITF_HID_CONSUMER = 1,
#if CONFIG_MIXR_USB_DEBUG_CDC
    ITF_CDC_CTRL = 2,
    ITF_CDC_DATA = 3,
#endif
    ITF_COUNT,
};

enum {
    HID_INST_VENDOR = 0,
    HID_INST_CONSUMER = 1,
};

#define EP_HID_VENDOR_OUT 0x01
#define EP_HID_VENDOR_IN 0x81
#define EP_HID_CONSUMER_IN 0x82
#define EP_CDC_NOTIF 0x83
#define EP_CDC_OUT 0x04
#define EP_CDC_IN 0x84

/* ---- Deskriptoren ----------------------------------------------------------------------------- */

/*
 * VID/PID: 0x1209:0x0001 ist die von pid.codes ausdrücklich für Tests/Prototypen freigegebene Kennung.
 * Vor einer Auslieferung MUSS eine eigene PID her — kostenlos via pid.codes (Open-Source-Lizenz nötig)
 * oder Espressif-PID-Programm (VID 0x303A, kostenlos für Produkte auf Espressif-Chips). Siehe README.
 */
#ifndef CONFIG_MIXR_USB_VID
#define CONFIG_MIXR_USB_VID 0x1209
#endif
#ifndef CONFIG_MIXR_USB_PID
#define CONFIG_MIXR_USB_PID 0x0001
#endif

static const uint8_t s_hid_report_desc_vendor[] = {
    TUD_HID_REPORT_DESC_GENERIC_INOUT(MIXR_HID_REPORT_SIZE),
};

static const uint8_t s_hid_report_desc_consumer[] = {
    TUD_HID_REPORT_DESC_CONSUMER(),
};

#if CONFIG_MIXR_USB_DEBUG_CDC
#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN + TUD_HID_DESC_LEN + TUD_CDC_DESC_LEN)
#else
#define CONFIG_TOTAL_LEN (TUD_CONFIG_DESC_LEN + TUD_HID_INOUT_DESC_LEN + TUD_HID_DESC_LEN)
#endif

enum {
    STR_LANG = 0,
    STR_MANUFACTURER,
    STR_PRODUCT,
    STR_SERIAL,
    STR_HID_VENDOR,
    STR_HID_CONSUMER,
    STR_CDC,
    STR_COUNT,
};

static const uint8_t s_config_desc[] = {
    TUD_CONFIG_DESCRIPTOR(1, ITF_COUNT, 0, CONFIG_TOTAL_LEN, 0, 500),
    /* Vendor-HID: 1 ms Polling → bis 64 KB/s je Richtung */
    TUD_HID_INOUT_DESCRIPTOR(ITF_HID_VENDOR, STR_HID_VENDOR, HID_ITF_PROTOCOL_NONE, sizeof(s_hid_report_desc_vendor),
                             EP_HID_VENDOR_OUT, EP_HID_VENDOR_IN, MIXR_HID_REPORT_SIZE, 1),
    /* Consumer Control: 8 ms reichen für Tastendrücke */
    TUD_HID_DESCRIPTOR(ITF_HID_CONSUMER, STR_HID_CONSUMER, HID_ITF_PROTOCOL_NONE, sizeof(s_hid_report_desc_consumer),
                       EP_HID_CONSUMER_IN, 8, 8),
#if CONFIG_MIXR_USB_DEBUG_CDC
    TUD_CDC_DESCRIPTOR(ITF_CDC_CTRL, STR_CDC, EP_CDC_NOTIF, 8, EP_CDC_OUT, EP_CDC_IN, 64),
#endif
};

static const tusb_desc_device_t s_device_desc = {
    .bLength = sizeof(tusb_desc_device_t),
    .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200,
#if CONFIG_MIXR_USB_DEBUG_CDC
    /* Composite mit IAD (CDC) → MISC/COMMON/IAD */
    .bDeviceClass = TUSB_CLASS_MISC,
    .bDeviceSubClass = MISC_SUBCLASS_COMMON,
    .bDeviceProtocol = MISC_PROTOCOL_IAD,
#else
    .bDeviceClass = 0x00,
    .bDeviceSubClass = 0x00,
    .bDeviceProtocol = 0x00,
#endif
    .bMaxPacketSize0 = CFG_TUD_ENDPOINT0_SIZE,
    .idVendor = CONFIG_MIXR_USB_VID,
    .idProduct = CONFIG_MIXR_USB_PID,
    .bcdDevice = 0x0100,
    .iManufacturer = STR_MANUFACTURER,
    .iProduct = STR_PRODUCT,
    .iSerialNumber = STR_SERIAL,
    .bNumConfigurations = 0x01,
};

static const char s_str_lang[] = {0x09, 0x04, 0x00}; /* English (US) */
static char s_str_serial[13] = "000000000000";
static const char *s_string_desc[STR_COUNT] = {
    s_str_lang,
    "Mixr",
    "Mixr Volume Mixer",
    s_str_serial,
    "Mixr Control",
    "Mixr Media Keys",
    "Mixr Debug Console",
};

/* ---- Zustand ---------------------------------------------------------------------------------- */

static mixr_frame_handler_t s_handler = nullptr;
static QueueHandle_t s_rx_reports = nullptr;   /* rohe 64-Byte-OUT-Reports vom USB-Task */
static SemaphoreHandle_t s_tx_mutex = nullptr;
static volatile bool s_mounted = false;
static uint32_t s_rx_dropped = 0;
static uint32_t s_rx_crc_errors = 0;

/* ---- TinyUSB-Callbacks (C-Linkage) ----------------------------------------------------------- */

extern "C" {

uint8_t const *tud_hid_descriptor_report_cb(uint8_t instance)
{
    return instance == HID_INST_CONSUMER ? s_hid_report_desc_consumer : s_hid_report_desc_vendor;
}

uint16_t tud_hid_get_report_cb(uint8_t, uint8_t, hid_report_type_t, uint8_t *, uint16_t)
{
    return 0; /* keine Feature-Reports */
}

void tud_hid_set_report_cb(uint8_t instance, uint8_t, hid_report_type_t, uint8_t const *buffer, uint16_t bufsize)
{
    if (instance != HID_INST_VENDOR || s_rx_reports == nullptr || bufsize == 0) {
        return;
    }
    uint8_t report[MIXR_HID_REPORT_SIZE] = {0};
    memcpy(report, buffer, bufsize < MIXR_HID_REPORT_SIZE ? bufsize : MIXR_HID_REPORT_SIZE);
    if (xQueueSend(s_rx_reports, report, 0) != pdTRUE) {
        s_rx_dropped++;
    }
}

} /* extern "C" */

static void usb_event_cb(tinyusb_event_t *event, void *)
{
    switch (event->id) {
        case TINYUSB_EVENT_ATTACHED:
            s_mounted = true;
            ESP_LOGI(TAG, "USB verbunden");
            break;
        case TINYUSB_EVENT_DETACHED:
            s_mounted = false;
            ESP_LOGI(TAG, "USB getrennt");
            break;
        default:
            break;
    }
}

/* ---- Empfang: Reports → Frames ---------------------------------------------------------------- */

static void comm_task(void *)
{
    uint8_t report[MIXR_HID_REPORT_SIZE];
    uint8_t frame[1 + MIXR_PAYLOAD_MAX + 2]; /* type + payload + crc16 */
    size_t frame_len = 0;
    bool in_frame = false;

    while (true) {
        if (xQueueReceive(s_rx_reports, report, portMAX_DELAY) != pdTRUE) {
            continue;
        }

        uint8_t flags = report[0];
        uint8_t n = report[1];
        if (n > MIXR_HID_REPORT_DATA_MAX) {
            in_frame = false;
            continue;
        }

        if (flags & MIXR_HID_FLAG_SOF) {
            frame_len = 0;
            in_frame = true;
        }
        if (!in_frame) {
            continue; /* Fortsetzung ohne Anfang (verlorener Report) */
        }
        if (frame_len + n > sizeof(frame)) {
            in_frame = false;
            continue;
        }
        memcpy(frame + frame_len, report + 2, n);
        frame_len += n;

        if (flags & MIXR_HID_FLAG_EOF) {
            in_frame = false;
            if (frame_len < 3) {
                continue;
            }
            size_t body = frame_len - 2;
            uint16_t want = (uint16_t)frame[body] | ((uint16_t)frame[body + 1] << 8);
            if (mixr_crc16(frame, body) != want) {
                s_rx_crc_errors++;
                continue;
            }
            if (s_handler != nullptr) {
                s_handler(static_cast<PktType>(frame[0]), frame + 1, (uint8_t)(body - 1));
            }
        }
    }
}

/* ---- Senden ----------------------------------------------------------------------------------- */

static bool wait_hid_ready(uint8_t instance, uint32_t timeout_ms)
{
    for (uint32_t waited = 0; waited < timeout_ms; waited++) {
        if (tud_hid_n_ready(instance)) {
            return true;
        }
        vTaskDelay(1);
    }
    return tud_hid_n_ready(instance);
}

void mixr_link_send(PktType type, const uint8_t *payload, uint8_t len)
{
    if (!s_mounted) {
        return;
    }

    uint8_t frame[1 + MIXR_PAYLOAD_MAX + 2];
    frame[0] = static_cast<uint8_t>(type);
    if (len > 0) {
        memcpy(frame + 1, payload, len);
    }
    size_t body = 1U + len;
    uint16_t crc = mixr_crc16(frame, body);
    frame[body] = (uint8_t)(crc & 0xFF);
    frame[body + 1] = (uint8_t)(crc >> 8);
    size_t total = body + 2;

    if (xSemaphoreTake(s_tx_mutex, pdMS_TO_TICKS(200)) != pdTRUE) {
        return;
    }

    size_t off = 0;
    bool first = true;
    while (off < total) {
        size_t n = total - off;
        if (n > MIXR_HID_REPORT_DATA_MAX) {
            n = MIXR_HID_REPORT_DATA_MAX;
        }
        uint8_t report[MIXR_HID_REPORT_SIZE] = {0};
        report[0] = (first ? MIXR_HID_FLAG_SOF : 0) | ((off + n == total) ? MIXR_HID_FLAG_EOF : 0);
        report[1] = (uint8_t)n;
        memcpy(report + 2, frame + off, n);

        if (!wait_hid_ready(HID_INST_VENDOR, 100) || !tud_hid_n_report(HID_INST_VENDOR, 0, report, sizeof(report))) {
            break; /* Host liest nicht — Frame verwerfen, nicht blockieren */
        }
        off += n;
        first = false;
    }

    xSemaphoreGive(s_tx_mutex);
}

bool mixr_link_send_consumer(uint16_t usage)
{
    if (!s_mounted) {
        return false;
    }
    if (xSemaphoreTake(s_tx_mutex, pdMS_TO_TICKS(200)) != pdTRUE) {
        return false;
    }
    bool ok = false;
    if (wait_hid_ready(HID_INST_CONSUMER, 50)) {
        ok = tud_hid_n_report(HID_INST_CONSUMER, 0, &usage, sizeof(usage));
        uint16_t release = 0;
        if (ok && wait_hid_ready(HID_INST_CONSUMER, 50)) {
            tud_hid_n_report(HID_INST_CONSUMER, 0, &release, sizeof(release));
        }
    }
    xSemaphoreGive(s_tx_mutex);
    return ok;
}

/* ---- Init ------------------------------------------------------------------------------------- */

void mixr_link_init(mixr_frame_handler_t handler)
{
    s_handler = handler;
    s_tx_mutex = xSemaphoreCreateMutex();
    s_rx_reports = xQueueCreate(96, MIXR_HID_REPORT_SIZE); /* 6 KiB: ≥ 1,5 Frames Puffer bei Cover-Bursts */

    uint8_t mac[6] = {0};
    esp_read_mac(mac, ESP_MAC_EFUSE_FACTORY);
    snprintf(s_str_serial, sizeof(s_str_serial), "%02X%02X%02X%02X%02X%02X", mac[0], mac[1], mac[2], mac[3], mac[4],
             mac[5]);

    tinyusb_config_t cfg = TINYUSB_DEFAULT_CONFIG(usb_event_cb);
    cfg.descriptor.device = &s_device_desc;
    cfg.descriptor.string = s_string_desc;
    cfg.descriptor.string_count = STR_COUNT;
    cfg.descriptor.full_speed_config = s_config_desc;
    ESP_ERROR_CHECK(tinyusb_driver_install(&cfg));

#if CONFIG_MIXR_USB_DEBUG_CDC
    tinyusb_config_cdcacm_t acm_cfg = {};
    acm_cfg.cdc_port = TINYUSB_CDC_ACM_0;
    ESP_ERROR_CHECK(tinyusb_cdcacm_init(&acm_cfg));
    ESP_ERROR_CHECK(tinyusb_console_init(TINYUSB_CDC_ACM_0));
#endif

    xTaskCreate(comm_task, "comm_task", 8192, nullptr, 5, nullptr);
    ESP_LOGI(TAG, "Link: USB-HID %04X:%04X, Serial %s", CONFIG_MIXR_USB_VID, CONFIG_MIXR_USB_PID, s_str_serial);
}

bool mixr_link_up(void)
{
    return s_mounted;
}

uint8_t mixr_link_caps(void)
{
    return MIXR_CAP_JPEG_COVER | MIXR_CAP_HID_CONSUMER | MIXR_CAP_BOOTLOADER_CMD | MIXR_CAP_LOG_STREAM;
}

const char *mixr_link_name(void)
{
    return "USB-HID";
}

#endif /* CONFIG_MIXR_USB_HID */
