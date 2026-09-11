#pragma once

#include "protocol.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/**
 * Firmware-Update über das Mixr-Protokoll (FW_BEGIN / FW_CHUNK / FW_END).
 *
 * Nur wenn eine zweite OTA-Partition existiert (partitions_ota.csv, ≥ 4 MiB):
 * esp_ota_* mit Rollback. Factory-only (aktuelles partitions.csv): FW_* → UNSUPPORTED.
 * Feld-Updates dann über ENTER_BOOTLOADER + esptool — PSRAM-Overwrite der laufenden
 * Factory hat USB nach dem 0.0.7→0.0.8-Update totgemacht.
 */

/** true, wenn ein echter OTA-Slot existiert (FW_* nutzbar). */
bool mixr_fw_update_supported(void);

/** true, solange ein Update läuft (Slider/Buttons pausieren, UI zeigt Fortschritt). */
bool mixr_fw_update_active(void);

/**
 * Verarbeitet FW_BEGIN / FW_CHUNK / FW_END / FW_ABORT. Sendet immer genau ein FW_ACK über send().
 * FW_END mit Erfolg startet das Gerät nach kurzer Verzögerung neu.
 */
void mixr_fw_update_handle(PktType type, const uint8_t *payload, uint8_t len,
                           void (*send)(PktType, const uint8_t *, uint8_t),
                           void (*progress)(uint8_t percent));

/** Nach USB-Trennung: halbes Update verwerfen. */
void mixr_fw_update_abort(void);

/** Beim Boot: laufende Firmware als gültig markieren (Rollback-Schutz, falls aktiviert). */
void mixr_fw_update_mark_valid(void);
