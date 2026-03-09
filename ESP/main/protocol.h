#pragma once

#include <stdint.h>

#define PKT_START_BYTE 0xAA

enum class PktType : uint8_t {
    SONG_TITLE = 0x01,
    SONG_ARTIST = 0x02,
    SLIDER_VALS = 0x03,
    BTN_CMD = 0x04,
    IMAGE_CHUNK = 0x05, 
    IMAGE_READY = 0x06  
};

enum class BtnCmd : uint8_t {
    BTN_0 = 0x00, // GPIO 33
    BTN_1 = 0x01, // GPIO 26
    BTN_2 = 0x02, // GPIO 38
    BTN_3 = 0x03, // GPIO 10
    BTN_4 = 0x04  // GPIO 4
};

struct UiMessage {
    PktType type;
    union {
        uint8_t slider_values[8];
        char text[64];
        BtnCmd command;
    } payload;
};