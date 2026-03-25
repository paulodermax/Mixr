#pragma once

#include <stdint.h>

#define PKT_START_BYTE 0xAA

/** Anzahl Fader auf der Mixr-Platine (MCP3008 Kanäle 0–3) */
#define MIXR_SLIDER_COUNT 4

/** Mindest-Differenz pro Kanal (0–255), sonst kein SLIDER_VALS (ADC-Rauschen filtern). */
#ifndef MIXR_SLIDER_DEADBAND
#define MIXR_SLIDER_DEADBAND 2
#endif

enum class PktType : uint8_t {
    SONG_TITLE = 0x01,
    SONG_ARTIST = 0x02,
    SLIDER_VALS = 0x03,
    BTN_CMD = 0x04,
    IMAGE_CHUNK = 0x05,
    IMAGE_READY = 0x06,
    /** ESP → PC: Nutzlast 1 Byte, siehe MediaSubCmd */
    MEDIA_CMD = 0x07,
    /** ESP → PC: Nutzlast 0 — PC löst Discord-VoIP-Mute (Hotkey), VK_9 / Strg+Linksshift+Alt+9 */
    VOIP_MUTE_CMD = 0x08,
    /** PC → ESP: Nutzlast 0 — Stumm-Icon toggeln (VK_9 / Strg+Linksshift+Alt+9) */
    VOIP_MUTE_TOGGLE_UI = 0x0A,
    /**
     * PC → ESP: Deafen-Icon toggeln; ESP → PC: Deafen-Hotkey auslösen.
     * Gleiches Byte 0x0B, Richtung getrennt — VK_0 / Strg+Linksshift+Alt+0.
     */
    VOIP_DEAFEN = 0x0B,
};

/** Nutzlast für PktType::MEDIA_CMD (gleiche Reihenfolge wie Playback-Menü). */
enum class MediaSubCmd : uint8_t {
    NEXT = 0,
    PLAY_PAUSE = 1,
    PREVIOUS = 2,
};

enum class BtnCmd : uint8_t {
    BTN_0 = 0x00,
    BTN_1 = 0x01,
    BTN_2 = 0x02,
    BTN_3 = 0x03,
    BTN_4 = 0x04
};

struct UiMessage {
    PktType type;
    union {
        uint8_t slider_values[MIXR_SLIDER_COUNT];
        char text[64];
        BtnCmd command;
    } payload;
};