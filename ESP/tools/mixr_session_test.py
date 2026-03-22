#!/usr/bin/env python3
"""
Test: eine konfigurierbare Player-Session (Titel, Artist, 240×240 Cover) ans Mixr-Display.

Nutzt dieselbe Protokoll-/USB-Logik wie mixr_send_demo.py (send_session_serial).

Vom Repo-Root:
  python tools/mixr_session_test.py COM6
  python tools/mixr_session_test.py COM6 --png tools/mein_cover.png --title "Live" --artist "Mixr"

idf.py monitor vorher beenden (ein COM-Port).
"""

from __future__ import annotations

import argparse
import sys
import time

from mixr_send_demo import (
    IMG_BYTES,
    close_serial_soft_exit,
    exit_if_placeholder_port,
    make_cover_gray_rgb565,
    make_cover_rgb565_from_png,
    open_serial_soft_exit,
    resolve_png_path,
    send_session_serial,
)


def main() -> None:
    p = argparse.ArgumentParser(
        description="Mixr: Test-Session (Metadaten + Cover) per USB an das Display senden",
    )
    p.add_argument(
        "port",
        help="Serieller Port (z. B. COM6), kein Platzhalter COMx",
    )
    p.add_argument("--title", default="Session-Test", help="Songtitel auf dem Display")
    p.add_argument("--artist", default="Mixr USB", help="Interpret")
    p.add_argument(
        "--png",
        metavar="FILE",
        help="Albumcover-PNG; ohne diese Option: einfarbiges Grau-Platzhalterbild",
    )
    p.add_argument("--baud", type=int, default=115200, help="Baudrate")
    p.add_argument(
        "--warmup-sec",
        type=float,
        default=0.5,
        metavar="S",
        help="Kurz warten nach Port-Open (USB)",
    )
    p.add_argument(
        "--chunk-delay-ms",
        type=float,
        default=4.0,
        metavar="MS",
        help="Pause zwischen Cover-Chunks (0 = schnellstmöglich)",
    )
    p.add_argument(
        "--post-send-ms",
        type=float,
        default=400.0,
        metavar="MS",
        help="Pause nach letztem Chunk",
    )
    p.add_argument(
        "--fast",
        action="store_true",
        help="Warmup/Chunk-Pause/Post-Send = 0 (max. Speed, vgl. alte Mixr-Bursts)",
    )
    args = p.parse_args()
    if args.fast:
        args.warmup_sec = 0.0
        args.chunk_delay_ms = 0.0
        args.post_send_ms = 0.0

    exit_if_placeholder_port(args.port)

    title = args.title.encode("utf-8")[:63]
    artist = args.artist.encode("utf-8")[:63]

    if args.png:
        png_path = resolve_png_path(args.png)
        raw = make_cover_rgb565_from_png(str(png_path))
        cover_desc = f"PNG {png_path.name}"
    else:
        raw = make_cover_gray_rgb565()
        cover_desc = "Grau-Platzhalter"

    assert len(raw) == IMG_BYTES

    ser = open_serial_soft_exit(args.port, args.baud, timeout=0.2)
    try:
        wu = max(0.0, args.warmup_sec)
        if wu > 0:
            time.sleep(wu)
        try:
            ser.reset_input_buffer()
        except (AttributeError, OSError):
            pass

        send_session_serial(
            ser,
            title,
            artist,
            raw,
            chunk_delay_ms=args.chunk_delay_ms,
            post_send_ms=args.post_send_ms,
        )

        print(
            f"Session auf Display: {title.decode('utf-8', errors='replace')!r} — "
            f"{artist.decode('utf-8', errors='replace')!r} ({cover_desc}, {IMG_BYTES} B).",
            flush=True,
        )
    finally:
        close_serial_soft_exit(ser)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nAbbruch.", file=sys.stderr)
        raise SystemExit(130)
