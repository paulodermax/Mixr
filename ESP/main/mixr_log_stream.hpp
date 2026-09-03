#pragma once

#include <stdint.h>

/**
 * Leitet ESP_LOG-Zeilen zusätzlich als LOG-Frames an den Host (opt-in per SET_LOG_STREAM).
 * Die Weiterleitung läuft in einem eigenen Task, damit der Log-Aufrufer (auch der USB-Task) nie
 * auf den Link wartet.
 */
void mixr_log_stream_init(void);

/** 0 = aus, 1 Error … 4 Debug. */
void mixr_log_stream_set_level(uint8_t level);

uint8_t mixr_log_stream_level(void);
