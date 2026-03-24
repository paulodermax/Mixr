#pragma once

#include "driver/gpio.h"
#include <atomic>
#include <stdint.h>

/**
 * KY-040 (typ. Breakout): mechanisch ~20 Rasten pro voller Umdrehung; jede
 * Raste hat eine Hemmschwelle und „klickt“. Software: ein Rastenschritt =
 * KY040_QUADRATURE_STEPS_PER_DETENT aufeinanderfolgende Quadratur-Zähler
 * (PJRC-Tabelle, voller Gray-Zyklus).
 */
#ifndef KY040_DETENTS_PER_REVOLUTION
#define KY040_DETENTS_PER_REVOLUTION 20
#endif
#ifndef KY040_QUADRATURE_STEPS_PER_DETENT
#define KY040_QUADRATURE_STEPS_PER_DETENT 4
#endif

/** KY-040: CLK/DT mit Pull-Up, SW active-low mit Pull-Up */
class Ky040Encoder {
public:
    void init(gpio_num_t clk, gpio_num_t dt, gpio_num_t sw);

    /**
     * Periodisch aufrufen (~1 ms), damit Quadratur nicht zwischen zwei
     * langsamen Hauptschleifen-Abfragen verloren geht.
     */
    void tick();

    /**
     * Netto-Rasten seit letztem Aufruf (eine Rastung = ein Menüschritt).
     * Kann |wert| > 1 sein bei schnellem Drehen.
     */
    int8_t read_detent_step();

    /** Kurzer Klick: Loslassen nach < 800 ms (nicht nach Langdruck) */
    bool consume_click();

    /** Einmal true, wenn Taster ≥ 800 ms gehalten */
    bool consume_long_press();

private:
    int8_t read_delta();

    gpio_num_t clk_{GPIO_NUM_NC};
    gpio_num_t dt_{GPIO_NUM_NC};
    gpio_num_t sw_{GPIO_NUM_NC};
    uint8_t prev_ab_{0};
    int detent_acc_{0};
    std::atomic<int32_t> pending_detents_{0};

    int last_sw_level_{1};
    uint32_t sw_down_ms_{0};
    bool sw_pressed_{false};
    bool long_emitted_{false};
    std::atomic<bool> pending_click_{false};
    std::atomic<bool> pending_long_{false};
};
