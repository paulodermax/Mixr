#pragma once

#include "protocol.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/**
 * Firmware-Update über das Mixr-Protokoll (FW_BEGIN / FW_CHUNK / FW_END).
 *
 * Läuft komplett im comm_task; schreibt mit esp_ota_* in die nächste OTA-Partition.
 * Ohne OTA-Partition (factory-only, z. B. 2-MiB-Flash) antwortet jede FW_*-Anfrage mit
 * FwStatus::UNSUPPORTED — der Host fällt dann auf den Download-Modus (esptool) zurück.
 */

/** true, wenn eine beschreibbare OTA-Partition existiert (HELLO caps). */
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
