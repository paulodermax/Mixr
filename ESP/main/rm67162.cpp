#include "rm67162.h"
#include "board_pins.h"
#include <string.h>
#include "esp_heap_caps.h"
#include "driver/spi_master.h"
#include "freertos/task.h"

// Map Arduino allocation to ESP-IDF SPIRAM allocation
#define ps_malloc(size) heap_caps_malloc(size, MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT)

const static lcd_cmd_t rm67162_qspi_init[] = {
    {0x11, {0x00}, 0x80}, 
    {0x3A, {0x55}, 0x01}, 
    {0x51, {0x00}, 0x01}, 
    {0x29, {0x00}, 0x80}, 
    {0x51, {0xD0}, 0x01}, 
};

static spi_device_handle_t spi;

static void lcd_send_cmd(uint32_t cmd, uint8_t *dat, uint32_t len)
{
    TFT_CS_L;
    spi_transaction_t t;
    memset(&t, 0, sizeof(t));
    t.flags = (SPI_TRANS_MULTILINE_CMD | SPI_TRANS_MULTILINE_ADDR);
    t.cmd = 0x02;
    t.addr = cmd << 8;
    
    if (len != 0) {
        t.tx_buffer = dat;
        t.length = 8 * len;
    } else {
        t.tx_buffer = NULL;
        t.length = 0;
    }
    
    spi_device_polling_transmit(spi, &t);
    TFT_CS_H;
}

void rm67162_init(void)
{
    pinMode(TFT_CS, OUTPUT);
    pinMode(TFT_RES, OUTPUT);

    TFT_RES_L;
    delay(300);
    TFT_RES_H;
    delay(200);

    esp_err_t ret;

    spi_bus_config_t buscfg;
    memset(&buscfg, 0, sizeof(buscfg));
    buscfg.data0_io_num = TFT_QSPI_D0;
    buscfg.data1_io_num = TFT_QSPI_D1;
    buscfg.sclk_io_num = TFT_QSPI_SCK;
    buscfg.data2_io_num = TFT_QSPI_D2;
    buscfg.data3_io_num = TFT_QSPI_D3;
    buscfg.max_transfer_sz = (SEND_BUF_SIZE * 16) + 8;
    buscfg.flags = SPICOMMON_BUSFLAG_MASTER | SPICOMMON_BUSFLAG_GPIO_PINS;

    spi_device_interface_config_t devcfg;
    memset(&devcfg, 0, sizeof(devcfg));
    devcfg.command_bits = 8;
    devcfg.address_bits = 24;
    devcfg.mode = TFT_SPI_MODE;
    devcfg.clock_speed_hz = SPI_FREQUENCY;
    devcfg.spics_io_num = -1;
    devcfg.flags = SPI_DEVICE_HALFDUPLEX;
    devcfg.queue_size = 17;

    ret = spi_bus_initialize(TFT_SPI_HOST, &buscfg, SPI_DMA_CH_AUTO);
    ESP_ERROR_CHECK(ret);
    
    ret = spi_bus_add_device(TFT_SPI_HOST, &devcfg, &spi);
    ESP_ERROR_CHECK(ret);

    int i = 3;
    while (i--) {
        const lcd_cmd_t *lcd_init = rm67162_qspi_init;
        for (int j = 0; j < sizeof(rm67162_qspi_init) / sizeof(lcd_cmd_t); j++) {
            lcd_send_cmd(lcd_init[j].cmd, (uint8_t *)lcd_init[j].data, lcd_init[j].len & 0x7f);
            if (lcd_init[j].len & 0x80) {
                delay(120);
            }
        }
    }
}

void lcd_setRotation(uint8_t r)
{
    uint8_t gbr = MIXR_LCD_MADCTL_BGR ? TFT_MAD_BGR : TFT_MAD_RGB;

    switch (r) {
    case 0:
        break;
    case 1:
        gbr = TFT_MAD_MX | TFT_MAD_MV | gbr;
        break;
    case 2:
        gbr = TFT_MAD_MX | TFT_MAD_MY | gbr;
        break;
    case 3:
        gbr = TFT_MAD_MV | TFT_MAD_MY | gbr;
        break;
    }
    lcd_send_cmd(TFT_MADCTL, &gbr, 1);
}

void lcd_address_set(uint16_t x1, uint16_t y1, uint16_t x2, uint16_t y2)
{
    lcd_cmd_t t[3] = {
        {0x2a, {(uint8_t)(x1 >> 8), (uint8_t)x1, (uint8_t)(x2 >> 8), (uint8_t)x2}, 0x04},
        {0x2b, {(uint8_t)(y1 >> 8), (uint8_t)y1, (uint8_t)(y2 >> 8), (uint8_t)y2}, 0x04},
        {0x2c, {0x00}, 0x00},
    };

    for (uint32_t i = 0; i < 3; i++) {
        lcd_send_cmd(t[i].cmd, t[i].data, t[i].len);
    }
}

void lcd_fill(uint16_t xsta, uint16_t ysta, uint16_t xend, uint16_t yend, uint16_t color)
{
    uint16_t w = xend - xsta;
    uint16_t h = yend - ysta;
    uint16_t *color_p = (uint16_t *)ps_malloc(w * h * 2);
    
    if (!color_p) return;
    
    memset(color_p, color, w * h * 2);
    lcd_PushColors(xsta, ysta, w, h, color_p);
    free(color_p);
}

void lcd_DrawPoint(uint16_t x, uint16_t y, uint16_t color)
{
    lcd_address_set(x, y, x + 1, y + 1);
    lcd_PushColors(&color, 1);
}

void lcd_PushColors(uint16_t x, uint16_t y, uint16_t width, uint16_t high, uint16_t *data)
{
    bool first_send = 1;
    size_t len = width * high;
    uint16_t *p = (uint16_t *)data;

    lcd_address_set(x, y, x + width - 1, y + high - 1);
    TFT_CS_L;
    do {
        size_t chunk_size = len;
        spi_transaction_ext_t t;
        memset(&t, 0, sizeof(t));
        
        if (first_send) {
            t.base.flags = SPI_TRANS_MODE_QIO;
            t.base.cmd = 0x32;
            t.base.addr = 0x002C00;
            first_send = 0;
        } else {
            t.base.flags = SPI_TRANS_MODE_QIO | SPI_TRANS_VARIABLE_CMD |
                           SPI_TRANS_VARIABLE_ADDR | SPI_TRANS_VARIABLE_DUMMY;
        }
        
        if (chunk_size > SEND_BUF_SIZE) {
            chunk_size = SEND_BUF_SIZE;
        }
        
        t.base.tx_buffer = p;
        t.base.length = chunk_size * 16;

        spi_device_polling_transmit(spi, (spi_transaction_t *)&t);
        len -= chunk_size;
        p += chunk_size;
        if (len > 0) {
            taskYIELD();
        }
    } while (len > 0);
    TFT_CS_H;
}

void lcd_PushColors(uint16_t *data, uint32_t len)
{
    bool first_send = 1;
    uint16_t *p = (uint16_t *)data;
    
    TFT_CS_L;
    do {
        size_t chunk_size = len;
        spi_transaction_ext_t t;
        memset(&t, 0, sizeof(t));
        
        if (first_send) {
            t.base.flags = SPI_TRANS_MODE_QIO;
            t.base.cmd = 0x32;
            t.base.addr = 0x002C00;
            first_send = 0;
        } else {
            t.base.flags = SPI_TRANS_MODE_QIO | SPI_TRANS_VARIABLE_CMD |
                           SPI_TRANS_VARIABLE_ADDR | SPI_TRANS_VARIABLE_DUMMY;
        }
        
        if (chunk_size > SEND_BUF_SIZE) {
            chunk_size = SEND_BUF_SIZE;
        }
        
        t.base.tx_buffer = p;
        t.base.length = chunk_size * 16;

        spi_device_polling_transmit(spi, (spi_transaction_t *)&t);
        len -= chunk_size;
        p += chunk_size;
        if (len > 0) {
            taskYIELD();
        }
    } while (len > 0);
    TFT_CS_H;
}

void lcd_sleep()
{
    lcd_send_cmd(0x10, NULL, 0);
}