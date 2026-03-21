#pragma once

/**
 * Mixr PCB (KiCad Mixr.dsn) — GPIO-Belegung ESP32-S3.
 *
 * Hinweis: Waveshare-Beispiel `pins_config.h` nutzt u. a. TFT_RES = GPIO 17.
 * Im DSN liegt KY-040 CLK ebenfalls auf GP17 (ESP6). Falls beides auf einer
 * Leiterplatte aktiv ist, einen der Signale umverdrahten oder die Software-Pins
 * anpassen — sonst Konflikt.
 */

#include "driver/gpio.h"

// MCP3008 (SPI3) — entspricht bisheriger Firmware
#define MIXR_SPI_HOST     SPI3_HOST
#define MIXR_PIN_SPI_MISO GPIO_NUM_12
#define MIXR_PIN_SPI_MOSI GPIO_NUM_11
#define MIXR_PIN_SPI_CLK  GPIO_NUM_13
#define MIXR_PIN_SPI_CS   GPIO_NUM_14

// Taster (active low, Pull-Up) — Netze /BTN_0 … /BTN_4
#define MIXR_PIN_BTN_0 GPIO_NUM_2
#define MIXR_PIN_BTN_1 GPIO_NUM_15
#define MIXR_PIN_BTN_2 GPIO_NUM_21
#define MIXR_PIN_BTN_3 GPIO_NUM_4
/** ESP11 / Modul-Pin 30 — bei Abweichung hier anpassen */
#define MIXR_PIN_BTN_4 GPIO_NUM_46

// KY-040 (Netze /PM_CLK, /PM_DT, /PM_SW)
#define MIXR_PIN_ENC_CLK GPIO_NUM_17
#define MIXR_PIN_ENC_DT  GPIO_NUM_16
#define MIXR_PIN_ENC_SW  GPIO_NUM_10

#define MIXR_BUTTON_GPIO_MASK                                                                 \
    ((1ULL << MIXR_PIN_BTN_0) | (1ULL << MIXR_PIN_BTN_1) | (1ULL << MIXR_PIN_BTN_2) |          \
     (1ULL << MIXR_PIN_BTN_3) | (1ULL << MIXR_PIN_BTN_4))

static inline const gpio_num_t mixr_button_gpios[5] = {
    MIXR_PIN_BTN_0,
    MIXR_PIN_BTN_1,
    MIXR_PIN_BTN_2,
    MIXR_PIN_BTN_3,
    MIXR_PIN_BTN_4,
};

/**
 * RM67162 QSPI + LVGL: Farben oft falsch, wenn (a) die beiden Bytes pro RGB565-Pixel
 * getauscht werden müssen und/oder (b) MADCTL Bit 3 (BGR) passt.
 * Bei Bedarf eine der Zeilen auf 0/1 umstellen und neu flashen.
 */
#ifndef MIXR_LCD_RGB565_SWAP_BYTES
#define MIXR_LCD_RGB565_SWAP_BYTES 1
#endif
#ifndef MIXR_LCD_MADCTL_BGR
#define MIXR_LCD_MADCTL_BGR 0
#endif
