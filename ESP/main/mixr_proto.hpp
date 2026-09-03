#pragma once

#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "protocol.h"

#include <stdbool.h>
#include <stdint.h>

/**
 * Verarbeitung eingehender Frames (läuft im comm_task des aktiven Links) und Zustände,
 * die Host und Gerät teilen: Cover-Empfang (RGB565/JPEG), Button-Map, Log-Stream, Firmware-Update.
 */

/** @param ui_queue   UiMessages an den LVGL-Task
 *  @param cover_buf  PSRAM-Puffer für das angezeigte Cover (MIXR_COVER_RGB565_BYTES) oder nullptr */
void mixr_proto_init(QueueHandle_t ui_queue, uint8_t *cover_buf);

/** Frame-Handler für mixr_link_init(). */
void mixr_proto_handle_frame(PktType type, const uint8_t *payload, uint8_t len);

/** HELLO senden (nach Verbindung und auf HELLO_REQ). */
void mixr_proto_send_hello(void);

/** Nach USB-Trennung: halbe Übertragungen verwerfen, Log-Stream aus. */
void mixr_proto_on_link_down(void);

/**
 * HID-Consumer-Usage für Taste `btn` (0..MIXR_BUTTON_COUNT-1); 0 = Host übernimmt (BTN_CMD).
 * Standard ohne Host: Prev / Play-Pause / Next / Mute / —.
 */
uint16_t mixr_proto_button_usage(int btn);

/** true, wenn der Host per SET_BUTTON_MAP eine Belegung geschickt hat. */
bool mixr_proto_button_map_from_host(void);
