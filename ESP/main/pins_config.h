#pragma once

#include <stdint.h>
#include "driver/gpio.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"

/**************************************************************
 * ARDUINO ZU ESP-IDF ÜBERSETZUNGS-MAKROS
 * Verhindert Fehler, da rm67162.cpp Arduino-Syntax nutzt
 **************************************************************/
#define delay(ms)              vTaskDelay(pdMS_TO_TICKS(ms))
#define OUTPUT                 GPIO_MODE_OUTPUT
#define pinMode(pin, mode)     gpio_reset_pin((gpio_num_t)pin); gpio_set_direction((gpio_num_t)pin, (gpio_mode_t)mode)
#define digitalWrite(pin, val) gpio_set_level((gpio_num_t)pin, val)


/**************************************************************
 * DISPLAY KONFIGURATION (Waveshare 1.91 AMOLED)
 **************************************************************/
#define LCD_USB_QSPI_DREVER   1       // Aktiviert den QSPI-Treiber in rm67162.cpp

#define SPI_FREQUENCY         75000000 // 75 MHz
#define TFT_SPI_MODE          0        // SPI_MODE0
#define TFT_SPI_HOST          SPI2_HOST

#define TFT_WIDTH             240
#define TFT_HEIGHT            536
#define EXAMPLE_LCD_H_RES     536
#define EXAMPLE_LCD_V_RES     240
#define LVGL_LCD_BUF_SIZE     (EXAMPLE_LCD_H_RES * EXAMPLE_LCD_V_RES)
#define SEND_BUF_SIZE         (0x4000)


/**************************************************************
 * PIN BELEGUNG (ESP32-S3 zu RM67162)
 **************************************************************/
// Allgemeine Steuerpins
#define TFT_RES               17
#define TFT_CS                6
#define TFT_DC                7
#define TFT_TE                9

// QSPI Datenbus (Für LCD_USB_QSPI_DREVER == 1)
#define TFT_QSPI_CS           6
#define TFT_QSPI_SCK          47
#define TFT_QSPI_D0           18
#define TFT_QSPI_D1           7
#define TFT_QSPI_D2           48
#define TFT_QSPI_D3           5

// Klassischer SPI Bus (Fallback, falls QSPI deaktiviert ist)
#define TFT_MOSI              18
#define TFT_SCK               47
#define TFT_SDO               8

// I2C für Touch (falls in Zukunft benötigt)
#define I2C_SCL               39
#define I2C_SDA               40