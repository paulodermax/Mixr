#pragma once

#include <stdint.h>

/*
 * Mixr Protokoll — Frame-Ebene
 * ============================
 *
 * Ein Frame besteht aus  type (u8) | payload[len]  mit len ≤ MIXR_PAYLOAD_MAX.
 * Wie ein Frame über USB transportiert wird, hängt vom Link ab:
 *
 *   USB-HID (Standard, CONFIG_MIXR_USB_HID):
 *     Frame + CRC-16/CCITT-FALSE (über type+payload, little endian angehängt) wird in
 *     64-Byte-Reports gestückelt:  [flags][n][data ≤ 62]   flags: bit0 = erster Report,
 *     bit1 = letzter Report. Vendor-HID, Usage Page 0xFF00. Kein Start-Byte nötig —
 *     Report-Grenzen liefert der USB-Stack.
 *
 *   USB-Serial/JTAG (Legacy-Build, für Boards ohne HID-Firmware und zum Debuggen):
 *     0xAA | len | type | payload | xor   (xor über len, type, payload)
 *
 * Der PC-Host (Mixr.Core/Services/MixrProtocol.cs, MixrFrameCodec.cs) spiegelt diese Datei.
 */
#define PKT_START_BYTE 0xAA

/** Protokollversion (HELLO). 1 = ohne HELLO; 2 = HELLO + Firmware-Update; 3 = HID-Link, JPEG-Cover, Button-Map, Log-Stream. */
#define MIXR_PROTOCOL_VERSION 3

/** Anzahl Fader auf der Mixr-Platine (MCP3008 Kanäle 0–3) */
#define MIXR_SLIDER_COUNT 4

/** Anzahl Taster */
#define MIXR_BUTTON_COUNT 5

/** Mindest-Differenz pro Kanal (0–255), sonst kein SLIDER_VALS (ADC-Rauschen filtern). */
#ifndef MIXR_SLIDER_DEADBAND
#define MIXR_SLIDER_DEADBAND 2
#endif

/** Maximale Nutzlast pro Frame (len ist u8). */
#define MIXR_PAYLOAD_MAX 255

/** Nutzdaten pro FW_CHUNK: 4 Byte Offset + Daten. */
#define MIXR_FW_CHUNK_DATA_MAX (MIXR_PAYLOAD_MAX - 4)

/** HID-Report-Größe (Vendor-Interface, IN und OUT). */
#define MIXR_HID_REPORT_SIZE 64
#define MIXR_HID_REPORT_DATA_MAX (MIXR_HID_REPORT_SIZE - 2)
#define MIXR_HID_FLAG_SOF 0x01
#define MIXR_HID_FLAG_EOF 0x02

/** Cover-Bild auf dem Display */
#define MIXR_COVER_W 240
#define MIXR_COVER_H 240
#define MIXR_COVER_RGB565_BYTES (MIXR_COVER_W * MIXR_COVER_H * 2)
/** Maximale JPEG-Größe, die das Gerät zwischenpuffert (PSRAM). */
#define MIXR_COVER_JPEG_MAX (96 * 1024)

enum class PktType : uint8_t {
    SONG_TITLE = 0x01,
    SONG_ARTIST = 0x02,
    SLIDER_VALS = 0x03,
    BTN_CMD = 0x04,
    /** PC → ESP: Bilddaten (Fortsetzung). Ohne IMAGE_BEGIN: rohes RGB565, 115200 Byte (Legacy). */
    IMAGE_CHUNK = 0x05,
    /** ESP-intern: Cover komplett → UI */
    IMAGE_READY = 0x06,
    /** ESP → PC: Nutzlast 1 Byte, siehe MediaSubCmd */
    MEDIA_CMD = 0x07,
    /** ESP → PC: Nutzlast 0 — PC löst Discord-VoIP-Mute (Hotkey) aus */
    VOIP_MUTE_CMD = 0x08,
    /** PC → ESP: Nutzlast 0 — Stumm-Icon toggeln */
    VOIP_MUTE_TOGGLE_UI = 0x0A,
    /** PC → ESP: Deafen-Icon toggeln; ESP → PC: Deafen-Hotkey auslösen (gleiches Byte, Richtung getrennt). */
    VOIP_DEAFEN = 0x0B,
    /** ESP → PC: Nutzlast 0 — Bildschirm teilen (Hotkey) */
    SHARE_SCREEN_CMD = 0x0C,

    /* ---- v2: Handshake + Firmware-Update ---- */

    /** PC → ESP: Nutzlast 0 — bitte HELLO senden. */
    HELLO_REQ = 0x10,
    /** ESP → PC: [proto_ver u8][caps u8][fw_version UTF-8, Rest]. caps: MIXR_CAP_*. */
    HELLO = 0x11,
    /** PC → ESP: [total_size u32 LE][sha256 32 B]. Antwort: FW_ACK. */
    FW_BEGIN = 0x12,
    /** PC → ESP: [offset u32 LE][data …]. Antwort: FW_ACK. */
    FW_CHUNK = 0x13,
    /** PC → ESP: Nutzlast 0 — Image prüfen, aktivieren, FW_ACK senden, neu starten. */
    FW_END = 0x14,
    /** ESP → PC: [status u8 (FwStatus)][next_offset u32 LE]. */
    FW_ACK = 0x15,
    /** PC → ESP: Nutzlast 0 — laufendes Update verwerfen. */
    FW_ABORT = 0x16,

    /* ---- v3: HID-Link, JPEG, Button-Map, Log-Stream ---- */

    /** PC → ESP: [format u8 (ImageFormat)][total u32 LE][hash u32 LE] — neues Cover beginnt. Antwort: IMAGE_ACK. */
    IMAGE_BEGIN = 0x20,
    /** PC → ESP: Nutzlast 0 — alle Chunks gesendet: dekodieren und anzeigen. */
    IMAGE_END = 0x21,
    /** ESP → PC: [status u8 (ImageAckStatus)][hash u32 LE]. */
    IMAGE_ACK = 0x22,
    /** PC → ESP: [MIXR_BUTTON_COUNT × u16 LE] HID-Consumer-Usage je Taste; 0 = nur BTN_CMD an den Host. */
    SET_BUTTON_MAP = 0x23,
    /** ESP → PC: [level u8][UTF-8 Text] — Firmware-Log (nur wenn SET_LOG_STREAM aktiv). */
    LOG = 0x24,
    /** PC → ESP: [level u8] — 0 aus, 1 Error, 2 Warn, 3 Info, 4 Debug. */
    SET_LOG_STREAM = 0x25,
    /** PC → ESP: Nutzlast 0 — Neustart in den ROM-Download-Modus (esptool). Kein ACK, Gerät verschwindet. */
    ENTER_BOOTLOADER = 0x26,
    /** PC → ESP: Nutzlast 0. Antwort PONG. */
    PING = 0x27,
    /** ESP → PC: [uptime_s u32 LE][free_heap u32 LE]. */
    PONG = 0x28,

    /** Nur ESP-intern (comm_task → UI-Queue): payload.slider_values[0] = Prozent. */
    FW_PROGRESS_UI = 0x7F,
};

/** HELLO caps-Bits */
#define MIXR_CAP_OTA_PROTOCOL 0x01  /* esp_ota-Partition vorhanden → FW_* nutzbar */
#define MIXR_CAP_JPEG_COVER 0x02    /* IMAGE_BEGIN mit ImageFormat::JPEG */
#define MIXR_CAP_HID_CONSUMER 0x04  /* Medientasten als HID Consumer Control (SET_BUTTON_MAP) */
#define MIXR_CAP_BOOTLOADER_CMD 0x08 /* ENTER_BOOTLOADER */
#define MIXR_CAP_LOG_STREAM 0x10    /* LOG / SET_LOG_STREAM */

enum class ImageFormat : uint8_t {
    RGB565 = 0, /* 240×240×2 Byte, little endian */
    JPEG = 1,   /* Baseline-JPEG 240×240 */
};

enum class ImageAckStatus : uint8_t {
    SEND_DATA = 0,     /* Cover unbekannt — Chunks bitte schicken */
    ALREADY_SHOWN = 1, /* Hash entspricht dem aktuell angezeigten Cover — Chunks überspringen */
    UNSUPPORTED = 2,   /* Format/Größe nicht möglich */
    DECODE_FAILED = 3, /* nach IMAGE_END: Dekodierung fehlgeschlagen */
    SHOWN = 4,         /* nach IMAGE_END: dekodiert und angezeigt */
};

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

/** HID Consumer-Control-Usages (USB HID Usage Tables, Page 0x0C) */
#define MIXR_HID_USAGE_NONE 0x0000
#define MIXR_HID_USAGE_PLAY_PAUSE 0x00CD
#define MIXR_HID_USAGE_SCAN_NEXT 0x00B5
#define MIXR_HID_USAGE_SCAN_PREV 0x00B6
#define MIXR_HID_USAGE_STOP 0x00B7
#define MIXR_HID_USAGE_MUTE 0x00E2
#define MIXR_HID_USAGE_VOL_UP 0x00E9
#define MIXR_HID_USAGE_VOL_DOWN 0x00EA

struct UiMessage {
    PktType type;
    union {
        uint8_t slider_values[MIXR_SLIDER_COUNT];
        char text[64];
        BtnCmd command;
    } payload;
};
