#include "encoder_ky040.hpp"
#include "esp_timer.h"

void Ky040Encoder::init(gpio_num_t clk, gpio_num_t dt, gpio_num_t sw)
{
    clk_ = clk;
    dt_ = dt;
    sw_ = sw;

    gpio_config_t io = {};
    io.pin_bit_mask = (1ULL << clk) | (1ULL << dt) | (1ULL << sw);
    io.mode = GPIO_MODE_INPUT;
    io.pull_up_en = GPIO_PULLUP_ENABLE;
    io.pull_down_en = GPIO_PULLDOWN_DISABLE;
    io.intr_type = GPIO_INTR_DISABLE;
    gpio_config(&io);

    prev_ab_ = (uint8_t)((gpio_get_level(clk_) << 1) | gpio_get_level(dt_));
    last_sw_ = gpio_get_level(sw_);
    pending_detents_.store(0);
    detent_acc_ = 0;
}

int8_t Ky040Encoder::read_delta()
{
    /* Vollständige Gray-Code-Tabelle (PJRC-ähnlich) */
    static const int8_t tbl[] = {
        0, -1, 1, 0, 1, 0, 0, -1, -1, 0, 0, 1, 0, 1, -1, 0
    };
    uint8_t a = (uint8_t)gpio_get_level(clk_);
    uint8_t b = (uint8_t)gpio_get_level(dt_);
    uint8_t cur = (uint8_t)((a << 1) | b);
    int8_t d = tbl[(prev_ab_ & 0x0F) << 2 | (cur & 0x03)];
    prev_ab_ = cur;
    return d;
}

void Ky040Encoder::tick()
{
    const int k = KY040_QUADRATURE_STEPS_PER_DETENT;
    int16_t net = 0;
    for (;;) {
        int8_t d = read_delta();
        if (d == 0) {
            break;
        }
        detent_acc_ += d;
        while (detent_acc_ >= k) {
            detent_acc_ -= k;
            net++;
        }
        while (detent_acc_ <= -k) {
            detent_acc_ += k;
            net--;
        }
    }
    if (net != 0) {
        pending_detents_.fetch_add(net);
    }
}

int8_t Ky040Encoder::read_detent_step()
{
    int32_t p = pending_detents_.exchange(0);
    if (p > 127) {
        p = 127;
    }
    if (p < -127) {
        p = -127;
    }
    return (int8_t)p;
}

bool Ky040Encoder::consume_click()
{
    int level = gpio_get_level(sw_);
    uint32_t now = (uint32_t)(esp_timer_get_time() / 1000);
    if (level == 0 && last_sw_ == 1) {
        if (now - last_sw_ms_ > 40) {
            last_sw_ms_ = now;
            last_sw_ = 0;
            return true;
        }
    }
    last_sw_ = level;
    return false;
}
