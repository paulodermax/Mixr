#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

/**
 * Dekodiert ein Baseline-JPEG (240×240) nach RGB565 little endian in `out` (MIXR_COVER_RGB565_BYTES).
 * Nutzt den in LVGL enthaltenen TJpgDec (CONFIG_LV_USE_TJPGD=y). Andere Bildgrößen werden zentriert
 * bzw. beschnitten; Rückgabe false bei Formatfehlern.
 */
bool mixr_cover_decode_jpeg(const uint8_t *jpeg, size_t jpeg_len, uint8_t *out_rgb565);
