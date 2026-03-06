#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "esp_log.h"
#include "rm67162.h"

static const char *TAG = "MAIN";

extern "C" void app_main(void)
{
    ESP_LOGI(TAG, "Init Display QSPI");
    rm67162_init();
    
    lcd_setRotation(1);

    ESP_LOGI(TAG, "Draw Test Color (Blue)");
    lcd_fill(0, 0, 536, 240, 0x001F); 

    while(1) {
        vTaskDelay(pdMS_TO_TICKS(5000));
    }
}