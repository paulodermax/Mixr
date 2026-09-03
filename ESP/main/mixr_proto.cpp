#include "mixr_proto.hpp"

#include "esp_app_desc.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "freertos/task.h"
#include "mixr_cover_jpeg.hpp"
#include "mixr_fw_update.hpp"
#include "mixr_link.hpp"
#include "mixr_log_stream.hpp"
#include "soc/rtc_cntl_reg.h"
#include "soc/soc.h"

#include <cstring>

static const char *TAG = "mixr_proto";

namespace {

QueueHandle_t s_ui_queue = nullptr;
uint8_t *s_cover = nullptr; /* RGB565, angezeigt */

/* ---- Cover-Empfang ---- */
struct CoverRx {
    bool active = false;
    ImageFormat format = ImageFormat::RGB565;
    uint32_t total = 0;
    uint32_t received = 0;
    uint32_t hash = 0;
};
CoverRx s_rx;
uint8_t *s_jpeg_buf = nullptr; /* PSRAM, MIXR_COVER_JPEG_MAX */
uint32_t s_shown_hash = 0;     /* Hash des zuletzt angezeigten Covers (0 = keins/unbekannt) */

/* ---- Button-Map ---- */
uint16_t s_button_usage[MIXR_BUTTON_COUNT] = {
    MIXR_HID_USAGE_SCAN_PREV, MIXR_HID_USAGE_PLAY_PAUSE, MIXR_HID_USAGE_SCAN_NEXT, MIXR_HID_USAGE_MUTE,
    MIXR_HID_USAGE_NONE,
};
bool s_button_map_from_host = false;

uint32_t rd_u32(const uint8_t *p)
{
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

void wr_u32(uint8_t *p, uint32_t v)
{
    p[0] = (uint8_t)v;
    p[1] = (uint8_t)(v >> 8);
    p[2] = (uint8_t)(v >> 16);
    p[3] = (uint8_t)(v >> 24);
}

void post_ui(PktType type)
{
    if (s_ui_queue == nullptr) {
        return;
    }
    UiMessage msg;
    msg.type = type;
    xQueueSend(s_ui_queue, &msg, 0);
}

void post_ui_text(PktType type, const uint8_t *payload, uint8_t len)
{
    if (s_ui_queue == nullptr) {
        return;
    }
    UiMessage msg;
    msg.type = type;
    uint8_t n = (len < sizeof(msg.payload.text) - 1) ? len : (uint8_t)(sizeof(msg.payload.text) - 1);
    memcpy(msg.payload.text, payload, n);
    msg.payload.text[n] = '\0';
    xQueueSend(s_ui_queue, &msg, 0);
}

void fw_progress_to_ui(uint8_t percent)
{
    if (s_ui_queue == nullptr) {
        return;
    }
    UiMessage msg;
    msg.type = PktType::FW_PROGRESS_UI;
    msg.payload.slider_values[0] = percent;
    xQueueSend(s_ui_queue, &msg, 0);
}

void send_image_ack(ImageAckStatus status, uint32_t hash)
{
    uint8_t p[5];
    p[0] = static_cast<uint8_t>(status);
    wr_u32(&p[1], hash);
    mixr_link_send(PktType::IMAGE_ACK, p, sizeof(p));
}

void cover_reset(void)
{
    s_rx = CoverRx{};
}

/* Legacy (Protokoll v1/v2): rohes RGB565 ohne IMAGE_BEGIN, komplett bei 115200 Byte. */
void cover_chunk_legacy(const uint8_t *payload, uint8_t len)
{
    if (s_cover == nullptr) {
        return;
    }
    if (!s_rx.active) {
        s_rx.active = true;
        s_rx.format = ImageFormat::RGB565;
        s_rx.total = MIXR_COVER_RGB565_BYTES;
        s_rx.received = 0;
        s_rx.hash = 0;
    }
    uint32_t n = len;
    if (s_rx.received + n > s_rx.total) {
        n = s_rx.total - s_rx.received;
    }
    memcpy(s_cover + s_rx.received, payload, n);
    s_rx.received += n;
    if (s_rx.received >= s_rx.total) {
        s_shown_hash = 0;
        cover_reset();
        post_ui(PktType::IMAGE_READY);
    }
}

void handle_image_begin(const uint8_t *payload, uint8_t len)
{
    if (len < 9) {
        send_image_ack(ImageAckStatus::UNSUPPORTED, 0);
        return;
    }
    auto format = static_cast<ImageFormat>(payload[0]);
    uint32_t total = rd_u32(payload + 1);
    uint32_t hash = rd_u32(payload + 5);

    cover_reset();
    if (s_cover == nullptr) {
        send_image_ack(ImageAckStatus::UNSUPPORTED, hash);
        return;
    }
    if (hash != 0 && hash == s_shown_hash) {
        send_image_ack(ImageAckStatus::ALREADY_SHOWN, hash);
        return;
    }

    if (format == ImageFormat::RGB565) {
        if (total != MIXR_COVER_RGB565_BYTES) {
            send_image_ack(ImageAckStatus::UNSUPPORTED, hash);
            return;
        }
    } else if (format == ImageFormat::JPEG) {
        if (total == 0 || total > MIXR_COVER_JPEG_MAX || s_jpeg_buf == nullptr) {
            send_image_ack(ImageAckStatus::UNSUPPORTED, hash);
            return;
        }
    } else {
        send_image_ack(ImageAckStatus::UNSUPPORTED, hash);
        return;
    }

    s_rx.active = true;
    s_rx.format = format;
    s_rx.total = total;
    s_rx.received = 0;
    s_rx.hash = hash;
    send_image_ack(ImageAckStatus::SEND_DATA, hash);
}

void handle_image_chunk(const uint8_t *payload, uint8_t len)
{
    if (!s_rx.active) {
        /* Kein IMAGE_BEGIN gesehen → Host mit Protokoll v1/v2 (rohes RGB565) */
        cover_chunk_legacy(payload, len);
        return;
    }

    uint32_t n = len;
    if (s_rx.received + n > s_rx.total) {
        n = s_rx.total - s_rx.received;
    }
    uint8_t *dst = (s_rx.format == ImageFormat::JPEG) ? s_jpeg_buf : s_cover;
    memcpy(dst + s_rx.received, payload, n);
    s_rx.received += n;

    /* Legacy-Host (v2) schickt kein IMAGE_END — RGB565 ist bei voller Größe fertig. */
    if (s_rx.format == ImageFormat::RGB565 && s_rx.received >= s_rx.total && s_rx.hash == 0) {
        s_shown_hash = 0;
        cover_reset();
        post_ui(PktType::IMAGE_READY);
    }
}

void handle_image_end(void)
{
    if (!s_rx.active) {
        send_image_ack(ImageAckStatus::DECODE_FAILED, 0);
        return;
    }
    uint32_t hash = s_rx.hash;
    bool ok = s_rx.received == s_rx.total;

    if (ok && s_rx.format == ImageFormat::JPEG) {
        int64_t t0 = esp_timer_get_time();
        ok = mixr_cover_decode_jpeg(s_jpeg_buf, s_rx.total, s_cover);
        ESP_LOGI(TAG, "JPEG %lu B → RGB565 in %lld ms (%s)", (unsigned long)s_rx.total,
                 (long long)((esp_timer_get_time() - t0) / 1000), ok ? "ok" : "Fehler");
    }

    cover_reset();
    if (ok) {
        s_shown_hash = hash;
        post_ui(PktType::IMAGE_READY);
        send_image_ack(ImageAckStatus::SHOWN, hash);
    } else {
        send_image_ack(ImageAckStatus::DECODE_FAILED, hash);
    }
}

void handle_set_button_map(const uint8_t *payload, uint8_t len)
{
    if (len < MIXR_BUTTON_COUNT * 2) {
        return;
    }
    for (int i = 0; i < MIXR_BUTTON_COUNT; i++) {
        s_button_usage[i] = (uint16_t)payload[i * 2] | ((uint16_t)payload[i * 2 + 1] << 8);
    }
    s_button_map_from_host = true;
    ESP_LOGI(TAG, "Button-Map vom Host: %04X %04X %04X %04X %04X", s_button_usage[0], s_button_usage[1],
             s_button_usage[2], s_button_usage[3], s_button_usage[4]);
}

void handle_pong_request(void)
{
    uint8_t p[8];
    wr_u32(&p[0], (uint32_t)(esp_timer_get_time() / 1000000));
    wr_u32(&p[4], (uint32_t)esp_get_free_heap_size());
    mixr_link_send(PktType::PONG, p, sizeof(p));
}

void enter_bootloader(void)
{
    ESP_LOGW(TAG, "Neustart in den ROM-Download-Modus (Host flasht mit esptool)");
    /* Zuerst ACK/Frame raus und USB trennen — sonst enumeriert Windows oft keinen COM-Port. */
    vTaskDelay(pdMS_TO_TICKS(50));
    mixr_link_prepare_bootloader();
    REG_WRITE(RTC_CNTL_OPTION1_REG, RTC_CNTL_FORCE_DOWNLOAD_BOOT);
    esp_restart();
}

} // namespace

void mixr_proto_init(QueueHandle_t ui_queue, uint8_t *cover_buf)
{
    s_ui_queue = ui_queue;
    s_cover = cover_buf;
    s_jpeg_buf = static_cast<uint8_t *>(heap_caps_malloc(MIXR_COVER_JPEG_MAX, MALLOC_CAP_SPIRAM));
    if (s_jpeg_buf == nullptr) {
        ESP_LOGW(TAG, "kein PSRAM für JPEG-Puffer — nur RGB565-Cover");
    }
}

void mixr_proto_send_hello(void)
{
    const esp_app_desc_t *desc = esp_app_get_description();
    uint8_t payload[2 + sizeof(desc->version)];
    payload[0] = MIXR_PROTOCOL_VERSION;
    payload[1] = mixr_link_caps() | (mixr_fw_update_supported() ? MIXR_CAP_OTA_PROTOCOL : 0);
    if (s_jpeg_buf == nullptr) {
        payload[1] &= (uint8_t)~MIXR_CAP_JPEG_COVER;
    }
    size_t vlen = strnlen(desc->version, sizeof(desc->version));
    memcpy(&payload[2], desc->version, vlen);
    mixr_link_send(PktType::HELLO, payload, (uint8_t)(2 + vlen));
}

void mixr_proto_on_link_down(void)
{
    cover_reset();
    mixr_fw_update_abort();
    mixr_log_stream_set_level(0);
    s_button_map_from_host = false;
}

uint16_t mixr_proto_button_usage(int btn)
{
    if (btn < 0 || btn >= MIXR_BUTTON_COUNT) {
        return MIXR_HID_USAGE_NONE;
    }
    return s_button_usage[btn];
}

bool mixr_proto_button_map_from_host(void)
{
    return s_button_map_from_host;
}

void mixr_proto_handle_frame(PktType type, const uint8_t *payload, uint8_t len)
{
    switch (type) {
        case PktType::SONG_TITLE:
        case PktType::SONG_ARTIST:
            /* Neuer Titel: halbes Legacy-Cover verwerfen (v3-Hosts schicken IMAGE_BEGIN ohnehin danach) */
            if (s_rx.active && s_rx.hash == 0) {
                cover_reset();
            }
            post_ui_text(type, payload, len);
            break;

        case PktType::IMAGE_BEGIN:
            handle_image_begin(payload, len);
            break;
        case PktType::IMAGE_CHUNK:
            handle_image_chunk(payload, len);
            break;
        case PktType::IMAGE_END:
            handle_image_end();
            break;

        case PktType::VOIP_MUTE_TOGGLE_UI:
        case PktType::VOIP_DEAFEN:
            if (len == 0) {
                post_ui(type);
            }
            break;

        case PktType::HELLO_REQ:
            mixr_proto_send_hello();
            break;

        case PktType::FW_BEGIN:
        case PktType::FW_CHUNK:
        case PktType::FW_END:
        case PktType::FW_ABORT:
            mixr_fw_update_handle(type, payload, len, mixr_link_send, fw_progress_to_ui);
            break;

        case PktType::SET_BUTTON_MAP:
            handle_set_button_map(payload, len);
            break;
        case PktType::SET_LOG_STREAM:
            if (len >= 1) {
                mixr_log_stream_set_level(payload[0]);
            }
            break;
        case PktType::ENTER_BOOTLOADER:
            enter_bootloader();
            break;
        case PktType::PING:
            handle_pong_request();
            break;

        default:
            /* Typen vom PC, die das Gerät nicht kennt (z. B. aus neueren Hosts) — ignorieren */
            break;
    }
}
