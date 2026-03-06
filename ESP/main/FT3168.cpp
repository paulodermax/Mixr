#include "FT3168.h"
#include "pins_config.h" 
#include "driver/i2c.h"
#include "esp_log.h"

#define I2C_MASTER_NUM I2C_NUM_0

FT3168::FT3168(int8_t sda_pin, int8_t scl_pin, int8_t rst_pin, int8_t int_pin)
{
    _sda = sda_pin;
    _scl = scl_pin;
    _rst = rst_pin;
    _int = int_pin;
}

void FT3168::begin(void)
{
    // 1. I2C Hardware initialisieren
    if (_sda != -1 && _scl != -1) {
        i2c_config_t conf = {};
        conf.mode = I2C_MODE_MASTER;
        conf.sda_io_num = _sda;
        conf.scl_io_num = _scl;
        conf.sda_pullup_en = GPIO_PULLUP_ENABLE;
        conf.scl_pullup_en = GPIO_PULLUP_ENABLE;
        conf.master.clk_speed = 400000;
        
        i2c_param_config(I2C_MASTER_NUM, &conf);
        i2c_driver_install(I2C_MASTER_NUM, conf.mode, 0, 0, 0);
        
        ESP_LOGI("FT3168", "I2C Bus initialisiert auf SDA:%d SCL:%d", _sda, _scl);
    }

    // 2. I2C Scanner
    ESP_LOGI("FT3168", "--- STARTE I2C SCANNER ---");
    int devices_found = 0;
    
    for (uint8_t i = 1; i < 127; i++) {
        i2c_cmd_handle_t cmd = i2c_cmd_link_create();
        i2c_master_start(cmd);
        i2c_master_write_byte(cmd, (i << 1) | I2C_MASTER_WRITE, true);
        i2c_master_stop(cmd);
        
        esp_err_t res = i2c_master_cmd_begin(I2C_MASTER_NUM, cmd, pdMS_TO_TICKS(10));
        i2c_cmd_link_delete(cmd);
        
        if (res == ESP_OK) {
            ESP_LOGW("FT3168", "-> Geraet gefunden auf Adresse: 0x%02X", i);
            devices_found++;
        }
    }
    
    if (devices_found == 0) {
        ESP_LOGE("FT3168", "KEINE I2C Geraete gefunden! Pins ueberpruefen.");
    }
    ESP_LOGI("FT3168", "--- SCAN BEENDET ---");

    // 3. Touch Controller wecken
    i2c_write(0x00, 0x00);
    delay(50);
}

uint8_t FT3168::i2c_read(uint8_t addr)
{
    uint8_t data = 0;
    i2c_master_write_read_device(I2C_MASTER_NUM, I2C_ADDR_FT3168, &addr, 1, &data, 1, pdMS_TO_TICKS(100));
    return data;
}

uint8_t FT3168::i2c_read_continuous(uint8_t addr, uint8_t *data, uint32_t length)
{
    esp_err_t err = i2c_master_write_read_device(I2C_MASTER_NUM, I2C_ADDR_FT3168, &addr, 1, data, length, pdMS_TO_TICKS(100));
    return (err == ESP_OK) ? 0 : -1;
}

void FT3168::i2c_write(uint8_t addr, uint8_t data)
{
    uint8_t write_buf[2] = {addr, data};
    i2c_master_write_to_device(I2C_MASTER_NUM, I2C_ADDR_FT3168, write_buf, sizeof(write_buf), pdMS_TO_TICKS(100));
}

bool FT3168::getTouch(uint16_t *x, uint16_t *y, uint8_t *gesture)
{
    uint8_t data[6];
    
    // Register 0x01: Gesture, 0x02: Finger count, 0x03-0x06: X/Y Data
    if (i2c_read_continuous(0x01, data, 6) == 0) {
        *gesture = data[0];
        uint8_t points = data[1] & 0x0F;
        
        if (points > 0) {
            *x = ((data[2] & 0x0F) << 8) | data[3];
            *y = ((data[4] & 0x0F) << 8) | data[5];
            return true;
        }
    }
    return false;
}