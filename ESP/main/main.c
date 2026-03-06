#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "driver/gpio.h"
#include "esp_log.h"

// Korrekte Pins für das 1.91" AMOLED laut deiner Datei
#define TFT_RES_PIN 17
#define TFT_CS_PIN  6

static const char *TAG = "AMOLED_HW";

void app_main(void)
{
    // Wir initialisieren nur Reset und Chip Select, um zu sehen, ob wir die Hardware "greifen"
    gpio_config_t io_conf = {
        .pin_bit_mask = (1ULL << TFT_RES_PIN) | (1ULL << TFT_CS_PIN),
        .mode = GPIO_MODE_OUTPUT,
        .pull_up_en = true
    };
    gpio_config(&io_conf);

    ESP_LOGI(TAG, "Starte Hardware-Reset Sequenz...");

    // AMOLED Reset Zyklus
    gpio_set_level(TFT_RES_PIN, 1);
    vTaskDelay(pdMS_TO_TICKS(100));
    gpio_set_level(TFT_RES_PIN, 0); // Reset aktiv
    vTaskDelay(pdMS_TO_TICKS(100));
    gpio_set_level(TFT_RES_PIN, 1); // Reset beendet
    vTaskDelay(pdMS_TO_TICKS(100));

    ESP_LOGI(TAG, "Hardware bereit. Naechster Schritt: QSPI Initialisierung.");

    while(1) {
        // Da wir keine LED haben, lassen wir den Log laufen
        ESP_LOGI(TAG, "Warte auf QSPI Befehle...");
        vTaskDelay(pdMS_TO_TICKS(2000));
    }
}