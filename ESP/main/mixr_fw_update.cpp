#include "mixr_fw_update.hpp"

#include "esp_attr.h"
#include "esp_cpu.h"
#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_ota_ops.h"
#include "esp_partition.h"
#include "esp_rom_spiflash.h"
#include "esp_rom_sys.h"
#include "esp_system.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "mbedtls/sha256.h"
#include "sdkconfig.h"

#include <cstring>

static const char *TAG = "mixr_fw";

namespace {

enum class FwMode : uint8_t {
    None = 0,
    Ota = 1,     /* esp_ota_* in freie OTA-Partition */
    Staging = 2, /* PSRAM-Puffer → Factory überschreiben */
};

struct FwSession {
    bool active = false;
    FwMode mode = FwMode::None;
    esp_ota_handle_t handle = 0;
    const esp_partition_t *target = nullptr;
    uint8_t *staging = nullptr;
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

void free_staging(void)
{
    if (s_fw.staging != nullptr) {
        heap_caps_free(s_fw.staging);
        s_fw.staging = nullptr;
    }
}

void reset_session(bool abort_ota)
{
    if (s_fw.active && abort_ota && s_fw.mode == FwMode::Ota && s_fw.handle != 0) {
        esp_ota_abort(s_fw.handle);
    }
    if (s_fw.active) {
        mbedtls_sha256_free(&s_fw.sha);
    }
    free_staging();
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
        esp_timer_start_once(t, 600 * 1000);
    } else {
        vTaskDelay(pdMS_TO_TICKS(600));
        esp_restart();
    }
}

/**
 * Factory-Image aus PSRAM flashen und neu starten.
 *
 * Wichtig: esp_flash_* mit DANGEROUS_WRITE_ABORTS bricht Schreibversuche auf die laufende
 * Partition ab — deshalb ROM-SPI-Flash-APIs aus IRAM. Nach dem ersten Erase darf kein
 * Flash-Code mehr laufen → nur ROM/IRAM, dann soft reset.
 */
static void IRAM_ATTR __attribute__((noreturn)) apply_staging_and_reboot(uint32_t flash_addr, const uint8_t *src,
                                                                         uint32_t size)
{
#if !CONFIG_FREERTOS_UNICORE
    esp_cpu_stall(1);
#endif

    const uint32_t erase_len = (size + 4095U) & ~4095U;
    /* Länge für ROM-Write auf 4 Byte ausrichten (Rest war 0xFF aus calloc/memset). */
    const uint32_t write_len = (size + 3U) & ~3U;

    esp_rom_spiflash_result_t r = esp_rom_spiflash_erase_area(flash_addr, erase_len);
    if (r == ESP_ROM_SPIFLASH_RESULT_OK) {
        r = esp_rom_spiflash_write(flash_addr, reinterpret_cast<const uint32_t *>(src), (int32_t)write_len);
    }

    (void)r;
    esp_rom_software_reset_system();
    while (true) {
    }
}

bool staging_possible_for(uint32_t total)
{
    if (total == 0) {
        return false;
    }
    const esp_partition_t *running = esp_ota_get_running_partition();
    if (running == nullptr || total > running->size) {
        return false;
    }
    size_t free_spiram = heap_caps_get_free_size(MALLOC_CAP_SPIRAM);
    /* +4 für 4-Byte-Ausrichtung, +256 KiB Reserve */
    return free_spiram >= (size_t)total + 4U + (256U * 1024U);
}

} // namespace

bool mixr_fw_update_supported(void)
{
    if (esp_ota_get_next_update_partition(nullptr) != nullptr) {
        return true;
    }
    return heap_caps_get_free_size(MALLOC_CAP_SPIRAM) >= (1024U * 1024U);
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

            if (len != 4 + 32) {
                send_ack(send, FwStatus::BEGIN_FAILED, 0);
                return;
            }

            uint32_t total = read_u32_le(payload);
            const esp_partition_t *ota = esp_ota_get_next_update_partition(nullptr);

            if (ota != nullptr) {
                if (total == 0 || total > ota->size) {
                    ESP_LOGW(TAG, "FW_BEGIN: %lu Byte passen nicht in %s (%lu)", (unsigned long)total, ota->label,
                             (unsigned long)ota->size);
                    send_ack(send, FwStatus::TOO_LARGE, 0);
                    return;
                }

                esp_ota_handle_t handle = 0;
                esp_err_t err = esp_ota_begin(ota, total, &handle);
                if (err != ESP_OK) {
                    ESP_LOGE(TAG, "esp_ota_begin: %s", esp_err_to_name(err));
                    send_ack(send, FwStatus::BEGIN_FAILED, 0);
                    return;
                }

                s_fw.active = true;
                s_fw.mode = FwMode::Ota;
                s_fw.handle = handle;
                s_fw.target = ota;
                s_fw.total = total;
                s_fw.written = 0;
                std::memcpy(s_fw.expected_sha, payload + 4, 32);
                mbedtls_sha256_init(&s_fw.sha);
                mbedtls_sha256_starts(&s_fw.sha, 0);
                ESP_LOGI(TAG, "OTA-Update: %lu Byte → %s", (unsigned long)total, ota->label);
                if (progress) {
                    progress(0);
                }
                send_ack(send, FwStatus::OK, 0);
                return;
            }

            if (!staging_possible_for(total)) {
                ESP_LOGW(TAG, "FW_BEGIN: kein OTA-Slot und Staging unmöglich (size=%lu, free_spiram=%u)",
                         (unsigned long)total, (unsigned)heap_caps_get_free_size(MALLOC_CAP_SPIRAM));
                send_ack(send, FwStatus::UNSUPPORTED, 0);
                return;
            }

            /* +3 Byte, damit ROM-Write 4-Byte-aligned enden kann (Padding 0xFF). */
            uint32_t alloc = (total + 3U) & ~3U;
            uint8_t *buf = static_cast<uint8_t *>(heap_caps_malloc(alloc, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT));
            if (buf == nullptr) {
                ESP_LOGE(TAG, "FW_BEGIN: PSRAM-Allokation %lu Byte fehlgeschlagen", (unsigned long)alloc);
                send_ack(send, FwStatus::BEGIN_FAILED, 0);
                return;
            }
            if (alloc > total) {
                std::memset(buf + total, 0xFF, alloc - total);
            }

            s_fw.active = true;
            s_fw.mode = FwMode::Staging;
            s_fw.target = esp_ota_get_running_partition();
            s_fw.staging = buf;
            s_fw.total = total;
            s_fw.written = 0;
            std::memcpy(s_fw.expected_sha, payload + 4, 32);
            mbedtls_sha256_init(&s_fw.sha);
            mbedtls_sha256_starts(&s_fw.sha, 0);
            ESP_LOGI(TAG, "Staging-Update: %lu Byte in PSRAM → Factory @0x%lx", (unsigned long)total,
                     (unsigned long)s_fw.target->address);
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
                send_ack(send, FwStatus::OUT_OF_SEQUENCE, s_fw.written);
                return;
            }
            if (s_fw.written + data_len > s_fw.total) {
                send_ack(send, FwStatus::TOO_LARGE, s_fw.written);
                return;
            }

            if (s_fw.mode == FwMode::Ota) {
                esp_err_t err = esp_ota_write(s_fw.handle, data, data_len);
                if (err != ESP_OK) {
                    ESP_LOGE(TAG, "esp_ota_write @%lu: %s", (unsigned long)offset, esp_err_to_name(err));
                    reset_session(true);
                    send_ack(send, FwStatus::WRITE_FAILED, 0);
                    return;
                }
            } else {
                std::memcpy(s_fw.staging + offset, data, data_len);
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

            if (s_fw.mode == FwMode::Ota) {
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

                ESP_LOGI(TAG, "OTA komplett (%lu Byte), Neustart …", (unsigned long)s_fw.total);
                if (progress) {
                    progress(100);
                }
                send_ack(send, FwStatus::OK, s_fw.total);
                reset_session(false);
                schedule_reboot();
                return;
            }

            ESP_LOGW(TAG, "Schreibe Factory aus PSRAM (%lu Byte @0x%lx) — USB nicht trennen!",
                     (unsigned long)s_fw.total, (unsigned long)s_fw.target->address);
            if (progress) {
                progress(100);
            }
            send_ack(send, FwStatus::OK, s_fw.total);
            vTaskDelay(pdMS_TO_TICKS(500));

            uint32_t addr = s_fw.target->address;
            uint32_t size = s_fw.total;
            const uint8_t *src = s_fw.staging;
            s_fw.active = false;
            apply_staging_and_reboot(addr, src, size);
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
