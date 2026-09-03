#pragma once

#include "protocol.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/**
 * Firmware-Update über das Mixr-Protokoll (FW_BEGIN / FW_CHUNK / FW_END).
 *
 * Zwei Wege (automatisch gewählt):
 *  1. OTA-Partition vorhanden (≥ 4 MiB, partitions_ota.csv) → esp_ota_* (sicher, Rollback möglich).
 *  2. Sonst (2-MiB factory-only): Image komplett in PSRAM puffern, SHA prüfen, dann die laufende
 *     Factory-Partition überschreiben und neu starten. Funktioniert über USB-HID ohne COM-Port /
 *     BOOT-Taste — Voraussetzung für zuverlässige Feld-Updates.
 *
 * Stromausfall mitten im Flashen kann das Gerät „stumm“ machen → dann einmalig BOOT+RESET + esptool.
 */

/** true, wenn FW_*-Updates möglich sind (OTA-Slot oder genug PSRAM zum Zwischenspeichern). */
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
