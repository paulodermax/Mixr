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
    last_sw_level_ = gpio_get_level(sw_);
    pending_detents_.store(0);
    detent_acc_ = 0;
    sw_pressed_ = false;
    long_emitted_ = false;
    pending_click_.store(false);
    pending_long_.store(false);
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

    /* Taster: Kurzklick bei Loslassen; Langdruck ≥ 800 ms */
    static const uint32_t k_long_hold_ms = 800;
    static const uint32_t k_debounce_ms = 40;
    int sw = gpio_get_level(sw_);
    uint32_t now = (uint32_t)(esp_timer_get_time() / 1000);
    if (sw == 0 && last_sw_level_ == 1) {
        sw_pressed_ = true;
        sw_down_ms_ = now;
        long_emitted_ = false;
    }
    if (sw == 0 && sw_pressed_) {
        if (!long_emitted_ && (now - sw_down_ms_ >= k_long_hold_ms)) {
            pending_long_.store(true);
            long_emitted_ = true;
        }
    }
    if (sw == 1 && last_sw_level_ == 0) {
        uint32_t dur = (now >= sw_down_ms_) ? (now - sw_down_ms_) : 0;
        if (sw_pressed_ && !long_emitted_ && dur >= k_debounce_ms && dur < k_long_hold_ms) {
            pending_click_.store(true);
        }
        sw_pressed_ = false;
        long_emitted_ = false;
    }
    last_sw_level_ = sw;
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
    return pending_click_.exchange(false);
}

bool Ky040Encoder::consume_long_press()
{
    return pending_long_.exchange(false);
}
