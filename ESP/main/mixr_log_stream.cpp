#include "mixr_log_stream.hpp"

#include "esp_log.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "mixr_link.hpp"

#include <cstdarg>
#include <cstdio>
#include <cstring>

namespace {

constexpr size_t kLineMax = 200; /* passt mit Level-Byte in ein Frame */

struct LogLine {
    uint8_t level;
    uint8_t len;
    char text[kLineMax];
};

QueueHandle_t s_queue = nullptr;
vprintf_like_t s_prev_vprintf = nullptr;
volatile uint8_t s_level = 0;

uint8_t level_from_line(const char *line)
{
    /* ESP-IDF-Format: "E (1234) tag: ..." / "W (" / "I (" / "D (" — ggf. mit Farbcode davor. */
    const char *p = line;
    if (p[0] == '\033') {
        const char *m = strchr(p, 'm');
        if (m != nullptr) {
            p = m + 1;
        }
    }
    switch (p[0]) {
        case 'E':
            return 1;
        case 'W':
            return 2;
        case 'I':
            return 3;
        case 'D':
        case 'V':
            return 4;
        default:
            return 3;
    }
}

int hooked_vprintf(const char *fmt, va_list args)
{
    int written = 0;
    if (s_prev_vprintf != nullptr) {
        va_list copy;
        va_copy(copy, args);
        written = s_prev_vprintf(fmt, copy);
        va_end(copy);
    }

    if (s_level == 0 || s_queue == nullptr || !mixr_link_up()) {
        return written;
    }

    LogLine line;
    int n = vsnprintf(line.text, sizeof(line.text), fmt, args);
    if (n <= 0) {
        return written;
    }
    if ((size_t)n >= sizeof(line.text)) {
        n = (int)sizeof(line.text) - 1;
    }
    /* Zeilenende und ANSI-Reset abschneiden */
    while (n > 0 && (line.text[n - 1] == '\n' || line.text[n - 1] == '\r')) {
        n--;
    }
    line.text[n] = '\0';
    line.level = level_from_line(line.text);
    if (line.level > s_level) {
        return written;
    }
    line.len = (uint8_t)n;
    xQueueSend(s_queue, &line, 0); /* bei Überlauf still verwerfen */
    return written;
}

void log_tx_task(void *)
{
    LogLine line;
    uint8_t payload[1 + kLineMax];
    while (true) {
        if (xQueueReceive(s_queue, &line, portMAX_DELAY) != pdTRUE) {
            continue;
        }
        payload[0] = line.level;
        memcpy(payload + 1, line.text, line.len);
        mixr_link_send(PktType::LOG, payload, (uint8_t)(1 + line.len));
    }
}

} // namespace

void mixr_log_stream_init(void)
{
    s_queue = xQueueCreate(16, sizeof(LogLine));
    xTaskCreate(log_tx_task, "log_tx", 3072, nullptr, 2, nullptr);
    s_prev_vprintf = esp_log_set_vprintf(hooked_vprintf);
}

void mixr_log_stream_set_level(uint8_t level)
{
    s_level = level > 4 ? 4 : level;
}

uint8_t mixr_log_stream_level(void)
{
    return s_level;
}
