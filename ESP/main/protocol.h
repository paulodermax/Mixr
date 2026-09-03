#pragma once

#include <stdint.h>

/*
 * Mixr Binärprotokoll (USB-Serial/JTAG, 921600 Baud)
 *
 *   Frame:  0xAA | len (u8) | type (u8) | payload[len] | crc (u8)
 *   crc  =  len ^ type ^ payload[0] ^ … ^ payload[len-1]
 *
 * Der PC-Host (Mixr.Core/Services/MixrProtocol.cs) spiegelt diese Konstanten.
 * MIXR_PROTOCOL_VERSION wird im HELLO ausgetauscht; inkompatible Änderungen erhöhen die Zahl.
 */
#define PKT_START_BYTE 0xAA

/** Protokollversion (HELLO). 1 = ursprüngliches Protokoll ohne HELLO; 2 = HELLO + Firmware-Update. */
#define MIXR_PROTOCOL_VERSION 2

/** Anzahl Fader auf der Mixr-Platine (MCP3008 Kanäle 0–3) */
#define MIXR_SLIDER_COUNT 4

/** Mindest-Differenz pro Kanal (0–255), sonst kein SLIDER_VALS (ADC-Rauschen filtern). */
#ifndef MIXR_SLIDER_DEADBAND
#define MIXR_SLIDER_DEADBAND 2
#endif

/** Maximale Nutzlast pro Frame (len ist u8). */
#define MIXR_PAYLOAD_MAX 255

/** Nutzdaten pro FW_CHUNK: 4 Byte Offset + Daten. */
#define MIXR_FW_CHUNK_DATA_MAX (MIXR_PAYLOAD_MAX - 4)

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
    /** ESP → PC: Nutzlast 0 — Bildschirm teilen (Hotkey), VK_8 / Strg+Linksshift+Alt+8 */
    SHARE_SCREEN_CMD = 0x0C,

    /* ---- Protokoll v2: Handshake + Firmware-Update ---- */

    /** PC → ESP: Nutzlast 0 — bitte HELLO senden. */
    HELLO_REQ = 0x10,
    /**
     * ESP → PC: [proto_ver u8][caps u8][fw_version UTF-8, Rest der Nutzlast].
     * caps: siehe MIXR_CAP_*.
     */
    HELLO = 0x11,
    /** PC → ESP: [total_size u32 LE][sha256 32 B] — Update starten. Antwort: FW_ACK. */
    FW_BEGIN = 0x12,
    /** PC → ESP: [offset u32 LE][data …] — Daten müssen lückenlos aufsteigend kommen. Antwort: FW_ACK. */
    FW_CHUNK = 0x13,
    /** PC → ESP: Nutzlast 0 — Image prüfen, aktivieren, FW_ACK senden, neu starten. */
    FW_END = 0x14,
    /** ESP → PC: [status u8 (FwStatus)][next_offset u32 LE]. */
    FW_ACK = 0x15,
    /** PC → ESP: Nutzlast 0 — laufendes Update verwerfen. */
    FW_ABORT = 0x16,

    /** Nur ESP-intern (comm_task → UI-Queue): payload.slider_values[0] = Prozent. */
    FW_PROGRESS_UI = 0x7F,
};

/** HELLO caps-Bits */
#define MIXR_CAP_OTA_PROTOCOL 0x01 /* esp_ota-Partition vorhanden → FW_* nutzbar */

enum class FwStatus : uint8_t {
    OK = 0,
    UNSUPPORTED = 1,  /* keine OTA-Partition (z. B. 2-MiB-Flash mit factory-only) */
    BEGIN_FAILED = 2,
    WRITE_FAILED = 3,
    VERIFY_FAILED = 4,
    OUT_OF_SEQUENCE = 5, /* Offset ≠ erwartet — Host sendet ab next_offset erneut */
    TOO_LARGE = 6,
    NOT_STARTED = 7,
    ABORTED = 8,
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
