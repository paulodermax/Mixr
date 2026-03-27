#!/usr/bin/env python3
"""
Sendet Titel, Artist und ein 240×240 RGB565-Cover an den Mixr (USB-Serial-JTAG-Protokoll).

Voraussetzungen:
  - pip install pyserial
  - Für --png: pip install pillow

Typischer Aufruf (im Ordner ESP, wie bei idf.py):
  python tools/mixr_send_demo.py COM6 --png ..\\tools\\mein_cover.png --title "Song" --artist "Band"

Vom Repo-Root (Mixr), PNG oft unter tools/:
  python tools/mixr_send_demo.py COM6 --png tools/mein_cover.png

Monitor vs. Senden (ein COM-Port):
  Unter Windows kann nur ein Programm den Port öffnen — idf.py monitor und dieses Skript
  schließen sich aus. Entweder Monitor beenden, senden, Monitor wieder starten; oder
  nach dem Senden kurz die Leitung mitlesen: --listen-sec 5

Hinweis rst:0x15 (USB_UART_CHIP_RESET):
  Wenn direkt nach dem Senden im RX plötzlich „ESP-ROM“ / Bootloader erscheint, hat der
  Chip neu gestartet. Zuerst --listen-sec 0 testen; --chunk-delay-ms erhöhen (8–12);
  --post-send-ms 500. Kein 100% Schutz bei einem USB möglich (siehe TESTING.md).

Cover-Zeit: grob (baud/255) × chunk-delay-ms Pause zwischen Paketen — bei Bedarf
  --chunk-delay-ms 2 oder 0 (max. Speed), wenn stabil.
"""

from __future__ import annotations

import argparse
import struct
import sys
import time
from pathlib import Path

try:
    import serial
except ImportError:
    print("Bitte installieren: pip install pyserial", file=sys.stderr)
    sys.exit(1)

PKT_START = 0xAA

SONG_TITLE = 0x01
SONG_ARTIST = 0x02
IMAGE_CHUNK = 0x05

IMG_W = IMG_H = 240
IMG_BYTES = IMG_W * IMG_H * 2
# Max. Nutzlast pro Paket (Len-Byte 0–255; 255 = weniger Roundtrips als 250)
CHUNK_MAX = 255


def resolve_png_path(user_path: str) -> Path:
    """Pfad zur PNG: CWD, Skriptverzeichnis, oder Mixr/tools/<Dateiname> (Repo-Layout)."""
    stem = Path(user_path).name
    here = Path(__file__).resolve().parent
    repo_root = here.parent.parent
    candidates = [
        Path(user_path).expanduser(),
        Path.cwd() / user_path,
        here / user_path,
        repo_root / "tools" / stem,
    ]
    seen: set[Path] = set()
    for c in candidates:
        try:
            r = c.resolve()
        except OSError:
            continue
        if r in seen:
            continue
        seen.add(r)
        if r.is_file():
            return r
    raise FileNotFoundError(
        f"PNG nicht gefunden: {user_path!r}\n"
        f"  Arbeitsverzeichnis: {Path.cwd()}\n"
        f"  Beispiel: --png tools\\{stem}"
    )


def build_packet(pkt_type: int, payload: bytes) -> bytes:
    ln = len(payload)
    if ln > CHUNK_MAX:
        raise ValueError(f"payload zu lang (max {CHUNK_MAX})")
    crc = ln ^ pkt_type
    for b in payload:
        crc ^= b
    return bytes([PKT_START, ln, pkt_type]) + payload + bytes([crc])


def rgb888_to_rgb565_le(r: int, g: int, b: int) -> bytes:
    """RGB565 wie LVGL/Little-Endian im Speicher (uint16 little-endian)."""
    v = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)
    return struct.pack("<H", v & 0xFFFF)


def make_cover_rgb565_from_png(path: str) -> bytes:
    try:
        from PIL import Image, ImageOps
    except ImportError:
        print("Für --png: pip install pillow", file=sys.stderr)
        sys.exit(1)

    try:
        _resample = Image.Resampling.LANCZOS
    except AttributeError:
        _resample = Image.LANCZOS

    im = Image.open(path).convert("RGBA")
    im = ImageOps.fit(im, (IMG_W, IMG_H), method=_resample, centering=(0.5, 0.5))
    bg = Image.new("RGB", (IMG_W, IMG_H), (0, 0, 0))
    bg.paste(im, mask=im.split()[3])
    im = bg

    out = bytearray(IMG_BYTES)
    px = im.load()
    i = 0
    for y in range(IMG_H):
        for x in range(IMG_W):
            r, g, b = px[x, y]
            out[i : i + 2] = rgb888_to_rgb565_le(r, g, b)
            i += 2
    assert i == IMG_BYTES
    return bytes(out)


def make_cover_gray_rgb565() -> bytes:
    return struct.pack("<H", 0x632C) * (IMG_W * IMG_H)


def exit_if_placeholder_port(port: str) -> None:
    """COMx in der Doku = Platzhalter; echten Port nötig (z. B. COM7)."""
    if port.strip().upper() == "COMX":
        print(
            "Ungültiger Port: 'COMx' ist nur ein Platzhalter in der Anleitung.\n"
            "  Ersetze ihn durch deinen echten Port, z. B. COM6 oder COM7.\n"
            "  Ports anzeigen:  python -m serial.tools.list_ports",
            file=sys.stderr,
        )
        raise SystemExit(2)


def open_serial_soft_exit(port: str, baud: int, timeout: float) -> serial.Serial:
    """Port öffnen mit DTR/RTS von Anfang an aus (kein erster High-Puls beim Open).

    PySerial setzt sonst _dtr_state=True; beim ersten SetCommState kann das unter
    Windows einen Reset/Neustart auslösen. Zuerst dtr/rts=False, dann open().
    """
    ser = serial.Serial()
    ser.baudrate = baud
    ser.timeout = timeout
    try:
        ser.write_timeout = 10
    except (AttributeError, ValueError):
        pass
    ser.dsrdtr = False
    ser.rtscts = False
    ser.dtr = False
    ser.rts = False
    ser.port = port
    ser.open()
    return ser


def close_serial_soft_exit(ser: serial.Serial) -> None:
    if ser is None:
        return
    try:
        ser.dtr = False
        ser.rts = False
    except (AttributeError, OSError, serial.SerialException):
        pass
    try:
        ser.close()
    except (OSError, serial.SerialException):
        pass


def send_session_serial(
    ser: serial.Serial,
    title: bytes,
    artist: bytes,
    raw_rgb565: bytes,
    *,
    chunk_delay_ms: float = 4.0,
    post_send_ms: float = 400.0,
) -> None:
    """Eine Player-Session (Titel, Artist, 240×240 RGB565-Cover) an den Mixr senden.

    Nutzt dasselbe Binärprotokoll wie die Firmware (SONG_TITLE, SONG_ARTIST, IMAGE_CHUNK).
    """
    if len(raw_rgb565) != IMG_BYTES:
        raise ValueError(f"Cover: erwartet {IMG_BYTES} B, haben {len(raw_rgb565)}")
    ser.write(build_packet(SONG_TITLE, title))
    ser.flush()
    ser.write(build_packet(SONG_ARTIST, artist))
    ser.flush()

    offset = 0
    chunk_delay = max(0.0, chunk_delay_ms / 1000.0)
    while offset < IMG_BYTES:
        n = min(CHUNK_MAX, IMG_BYTES - offset)
        ser.write(build_packet(IMAGE_CHUNK, raw_rgb565[offset : offset + n]))
        offset += n
        if chunk_delay > 0 and offset < IMG_BYTES:
            time.sleep(chunk_delay)
    ser.flush()
    post = max(0.0, post_send_ms / 1000.0)
    if post > 0:
        time.sleep(post)


def main() -> None:
    p = argparse.ArgumentParser(description="Mixr: Titel / Artist / Cover per USB")
    p.add_argument(
        "port",
        help="Serieller Port (kein Platzhalter): z.B. COM7 unter Windows oder /dev/ttyACM0",
    )
    p.add_argument("--title", default="Test Titel", help="Songtitel")
    p.add_argument("--artist", default="Test Artist", help="Interpret")
    p.add_argument("--png", metavar="FILE", help="Albumcover (beliebiges Seitenverhältnis → 240×240, beschnitten)")
    p.add_argument("--baud", type=int, default=921600, help="Baud (Standard 921600)")
    p.add_argument(
        "--listen-sec",
        type=float,
        default=0.0,
        metavar="S",
        help="Nach dem Senden S Sekunden serielle RX ausgeben (ESP-Logs; 0=aus). Ohne zweiten Monitor.",
    )
    p.add_argument(
        "--chunk-delay-ms",
        type=float,
        default=4.0,
        metavar="MS",
        help="Pause zwischen IMAGE_CHUNK-Paketen (ms). ~452 Pakete: 4 ms ≈ 1,8 s reine Pause; bei Reset risiko höher setzen. 0=max. Speed.",
    )
    p.add_argument(
        "--warmup-sec",
        type=float,
        default=0.5,
        metavar="S",
        help="Nach Port-Open warten (USB stabil), bevor gesendet wird. 0=sofort.",
    )
    p.add_argument(
        "--post-send-ms",
        type=float,
        default=400.0,
        metavar="MS",
        help="Nach letztem Cover-Chunk: Pause bevor ausgegeben/gelauscht wird (USB/ESP puffer leeren).",
    )
    p.add_argument(
        "--fast",
        action="store_true",
        help="Warmup/Chunk-Pause/Post-Send auf 0 (Burst wie früher — schnell, bei Problemen ohne --fast testen).",
    )
    args = p.parse_args()
    if args.fast:
        args.warmup_sec = 0.0
        args.chunk_delay_ms = 0.0
        args.post_send_ms = 0.0

    title = args.title.encode("utf-8")[:63]
    artist = args.artist.encode("utf-8")[:63]

    if args.png:
        png_file = resolve_png_path(args.png)
        raw = make_cover_rgb565_from_png(str(png_file))
    else:
        raw = make_cover_gray_rgb565()

    assert len(raw) == IMG_BYTES

    exit_if_placeholder_port(args.port)
    ser = open_serial_soft_exit(args.port, args.baud, timeout=0.2)
    try:
        wu = max(0.0, args.warmup_sec)
        if wu > 0:
            time.sleep(wu)
        try:
            ser.reset_input_buffer()
        except (AttributeError, OSError, serial.SerialException):
            pass
        send_session_serial(
            ser,
            title,
            artist,
            raw,
            chunk_delay_ms=args.chunk_delay_ms,
            post_send_ms=args.post_send_ms,
        )

        src = f"PNG {args.png!r}" if args.png else "Grau-Platzhalter"
        print(
            f"Gesendet: Titel ({len(title)} B), Artist ({len(artist)} B), Cover ({IMG_BYTES} B, {src}).",
            flush=True,
        )

        if args.listen_sec > 0:
            print(
                f"--- {args.listen_sec}s RX (ESP-Logs auf stderr, ggf. binäre Reste vom Protokoll) ---",
                file=sys.stderr,
                flush=True,
            )
            deadline = time.monotonic() + args.listen_sec
            line_buf = ""
            try:
                while time.monotonic() < deadline:
                    chunk = ser.read(4096)
                    if chunk:
                        line_buf += chunk.decode("utf-8", errors="replace")
                        while "\n" in line_buf:
                            line, line_buf = line_buf.split("\n", 1)
                            print(line, file=sys.stderr, flush=True)
                    else:
                        time.sleep(0.02)
                if line_buf.strip():
                    print(line_buf, file=sys.stderr, flush=True)
            except KeyboardInterrupt:
                print("\n--- Abbruch ---", file=sys.stderr, flush=True)
    finally:
        close_serial_soft_exit(ser)


if __name__ == "__main__":
    main()
