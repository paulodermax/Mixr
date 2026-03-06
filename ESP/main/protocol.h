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
    PLAY_PAUSE = 0x01,
    NEXT = 0x02,
    PREV = 0x03
};

struct UiMessage {
    PktType type;
    union {
        uint8_t slider_values[4];
        char text[64];
        BtnCmd command;
    } payload;
};