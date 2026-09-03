#pragma once

#include "protocol.h"

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/**
 * Transport-Abstraktion für die Verbindung zum PC.
 *
 * Backend wird per Kconfig gewählt:
 *   CONFIG_MIXR_USB_HID   → mixr_link_usb.cpp    (TinyUSB: Vendor-HID + Consumer-Control-HID [+ Debug-CDC])
 *   sonst                 → mixr_link_serial.cpp (USB-Serial/JTAG, 0xAA-Framing, Legacy)
 *
 * Empfangene Frames werden im comm_task (nicht im USB-Task) an den Handler übergeben.
 */

typedef void (*mixr_frame_handler_t)(PktType type, const uint8_t *payload, uint8_t len);

/** Backend starten. Handler wird für jedes vollständige, geprüfte Frame aufgerufen. */
void mixr_link_init(mixr_frame_handler_t handler);

/** true, wenn der PC verbunden ist (HID: vom Host konfiguriert; Serial: usb_serial_jtag_is_connected). */
bool mixr_link_up(void);

/** Frame senden. Threadsicher; blockiert kurz, verwirft bei hängendem Host statt zu blockieren. */
void mixr_link_send(PktType type, const uint8_t *payload, uint8_t len);

/** Fähigkeiten des aktiven Backends für HELLO (MIXR_CAP_*). */
uint8_t mixr_link_caps(void);

/** HID Consumer-Control-Taste drücken und loslassen (nur HID-Backend, sonst no-op / false). */
bool mixr_link_send_consumer(uint16_t usage);

/** Beschreibung fürs Log / Debug-Menü, z. B. "USB-HID" oder "USB-Serial/JTAG". */
const char *mixr_link_name(void);

/* ---- CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF) — vom HID-Backend und den Tests genutzt ---- */
uint16_t mixr_crc16(const uint8_t *data, size_t len);
uint16_t mixr_crc16_update(uint16_t crc, const uint8_t *data, size_t len);
