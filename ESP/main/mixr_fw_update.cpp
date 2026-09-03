#include "mixr_fw_update.hpp"

#include "esp_log.h"
#include "esp_ota_ops.h"
#include "esp_partition.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "mbedtls/sha256.h"

#include <cstring>

static const char *TAG = "mixr_fw";

namespace {

struct FwSession {
    bool active = false;
    esp_ota_handle_t handle = 0;
    const esp_partition_t *target = nullptr;
    uint32_t total = 0;
    uint32_t written = 0;
    uint8_t expected_sha[32] = {0};
    mbedtls_sha256_context sha;
    uint8_t last_percent = 255;
};

FwSession s_fw;

uint32_t read_u32_le(const uint8_t *p)
{
    return (uint32_t)p[0] | ((uint32_t)p[1] << 8) | ((uint32_t)p[2] << 16) | ((uint32_t)p[3] << 24);
}

void write_u32_le(uint8_t *p, uint32_t v)
{
    p[0] = (uint8_t)(v & 0xFF);
    p[1] = (uint8_t)((v >> 8) & 0xFF);
    p[2] = (uint8_t)((v >> 16) & 0xFF);
    p[3] = (uint8_t)((v >> 24) & 0xFF);
}

void send_ack(void (*send)(PktType, const uint8_t *, uint8_t), FwStatus status, uint32_t next_offset)
{
    uint8_t p[5];
    p[0] = static_cast<uint8_t>(status);
    write_u32_le(&p[1], next_offset);
    send(PktType::FW_ACK, p, sizeof(p));
}

void reset_session(bool abort_ota)
{
    if (s_fw.active && abort_ota && s_fw.handle != 0) {
        esp_ota_abort(s_fw.handle);
    }
    if (s_fw.active) {
        mbedtls_sha256_free(&s_fw.sha);
    }
    s_fw = FwSession{};
}

void reboot_timer_cb(void *)
{
    esp_restart();
}

void schedule_reboot(void)
{
    const esp_timer_create_args_t args = {
        .callback = &reboot_timer_cb,
        .arg = nullptr,
        .dispatch_method = ESP_TIMER_TASK,
        .name = "fw_reboot",
        .skip_unhandled_events = true,
    };
    esp_timer_handle_t t;
    if (esp_timer_create(&args, &t) == ESP_OK) {
        /* ACK muss erst über USB raus, sonst sieht der Host nur den Disconnect. */
        esp_timer_start_once(t, 600 * 1000);
    } else {
        vTaskDelay(pdMS_TO_TICKS(600));
        esp_restart();
    }
}

} // namespace

bool mixr_fw_update_supported(void)
{
    return esp_ota_get_next_update_partition(nullptr) != nullptr;
}

bool mixr_fw_update_active(void)
{
    return s_fw.active;
}

void mixr_fw_update_abort(void)
{
    if (s_fw.active) {
        ESP_LOGW(TAG, "Update abgebrochen bei %lu/%lu Byte", (unsigned long)s_fw.written,
                 (unsigned long)s_fw.total);
    }
    reset_session(true);
}

void mixr_fw_update_mark_valid(void)
{
    /* Nur relevant, wenn CONFIG_BOOTLOADER_APP_ROLLBACK_ENABLE gesetzt ist; sonst no-op mit Fehlercode. */
    esp_ota_img_states_t state;
    const esp_partition_t *running = esp_ota_get_running_partition();
    if (running != nullptr && esp_ota_get_state_partition(running, &state) == ESP_OK
        && state == ESP_OTA_IMG_PENDING_VERIFY) {
        esp_ota_mark_app_valid_cancel_rollback();
        ESP_LOGI(TAG, "Neue Firmware als gültig markiert");
    }
}

void mixr_fw_update_handle(PktType type, const uint8_t *payload, uint8_t len,
                           void (*send)(PktType, const uint8_t *, uint8_t),
                           void (*progress)(uint8_t percent))
{
    switch (type) {
        case PktType::FW_BEGIN: {
            reset_session(true);

            const esp_partition_t *target = esp_ota_get_next_update_partition(nullptr);
            if (target == nullptr) {
                ESP_LOGW(TAG, "FW_BEGIN: keine OTA-Partition (factory-only)");
                send_ack(send, FwStatus::UNSUPPORTED, 0);
                return;
            }
            if (len != 4 + 32) {
                send_ack(send, FwStatus::BEGIN_FAILED, 0);
                return;
            }

            uint32_t total = read_u32_le(payload);
            if (total == 0 || total > target->size) {
                ESP_LOGW(TAG, "FW_BEGIN: %lu Byte passen nicht in %s (%lu)", (unsigned long)total,
                         target->label, (unsigned long)target->size);
                send_ack(send, FwStatus::TOO_LARGE, 0);
                return;
            }

            esp_ota_handle_t handle = 0;
            esp_err_t err = esp_ota_begin(target, total, &handle);
            if (err != ESP_OK) {
                ESP_LOGE(TAG, "esp_ota_begin: %s", esp_err_to_name(err));
                send_ack(send, FwStatus::BEGIN_FAILED, 0);
                return;
            }

            s_fw.active = true;
            s_fw.handle = handle;
            s_fw.target = target;
            s_fw.total = total;
            s_fw.written = 0;
            std::memcpy(s_fw.expected_sha, payload + 4, 32);
            mbedtls_sha256_init(&s_fw.sha);
            mbedtls_sha256_starts(&s_fw.sha, 0);
            ESP_LOGI(TAG, "Update gestartet: %lu Byte → %s", (unsigned long)total, target->label);
            if (progress) {
                progress(0);
            }
            send_ack(send, FwStatus::OK, 0);
            return;
        }

        case PktType::FW_CHUNK: {
            if (!s_fw.active) {
                send_ack(send, FwStatus::NOT_STARTED, 0);
                return;
            }
            if (len < 4) {
                send_ack(send, FwStatus::WRITE_FAILED, s_fw.written);
                return;
            }
            uint32_t offset = read_u32_le(payload);
            const uint8_t *data = payload + 4;
            uint32_t data_len = (uint32_t)len - 4;

            if (offset != s_fw.written) {
                /* Doppelt gesendeter oder verlorener Frame: Host synchronisiert sich auf next_offset. */
                send_ack(send, FwStatus::OUT_OF_SEQUENCE, s_fw.written);
                return;
            }
            if (s_fw.written + data_len > s_fw.total) {
                send_ack(send, FwStatus::TOO_LARGE, s_fw.written);
                return;
            }

            esp_err_t err = esp_ota_write(s_fw.handle, data, data_len);
            if (err != ESP_OK) {
                ESP_LOGE(TAG, "esp_ota_write @%lu: %s", (unsigned long)offset, esp_err_to_name(err));
                reset_session(true);
                send_ack(send, FwStatus::WRITE_FAILED, 0);
                return;
            }
            mbedtls_sha256_update(&s_fw.sha, data, data_len);
            s_fw.written += data_len;

            uint8_t pct = (uint8_t)((uint64_t)s_fw.written * 100U / s_fw.total);
            if (pct != s_fw.last_percent && progress) {
                s_fw.last_percent = pct;
                progress(pct);
            }
            send_ack(send, FwStatus::OK, s_fw.written);
            return;
        }

        case PktType::FW_END: {
            if (!s_fw.active) {
                send_ack(send, FwStatus::NOT_STARTED, 0);
                return;
            }
            if (s_fw.written != s_fw.total) {
                send_ack(send, FwStatus::OUT_OF_SEQUENCE, s_fw.written);
                return;
            }

            uint8_t actual[32];
            mbedtls_sha256_finish(&s_fw.sha, actual);
            if (std::memcmp(actual, s_fw.expected_sha, 32) != 0) {
                ESP_LOGE(TAG, "SHA-256 stimmt nicht — Image verworfen");
                reset_session(true);
                send_ack(send, FwStatus::VERIFY_FAILED, 0);
                return;
            }

            esp_err_t err = esp_ota_end(s_fw.handle);
            s_fw.handle = 0;
            if (err != ESP_OK) {
                ESP_LOGE(TAG, "esp_ota_end: %s", esp_err_to_name(err));
                reset_session(false);
                send_ack(send, FwStatus::VERIFY_FAILED, 0);
                return;
            }
            err = esp_ota_set_boot_partition(s_fw.target);
            if (err != ESP_OK) {
                ESP_LOGE(TAG, "esp_ota_set_boot_partition: %s", esp_err_to_name(err));
                reset_session(false);
                send_ack(send, FwStatus::WRITE_FAILED, 0);
                return;
            }

            ESP_LOGI(TAG, "Update komplett (%lu Byte), Neustart …", (unsigned long)s_fw.total);
            if (progress) {
                progress(100);
            }
            send_ack(send, FwStatus::OK, s_fw.total);
            reset_session(false);
            schedule_reboot();
            return;
        }

        case PktType::FW_ABORT:
            mixr_fw_update_abort();
            send_ack(send, FwStatus::ABORTED, 0);
            return;

        default:
            return;
    }
}
