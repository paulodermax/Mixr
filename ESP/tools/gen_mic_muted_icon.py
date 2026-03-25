#!/usr/bin/env python3
"""Erzeugt mic_muted_icon.c (RGB565) aus PNG — z. B. MicMuted.svg via node_raster/raster_mic_muted.cjs."""
import argparse
import os
import sys

try:
    from PIL import Image
except ImportError:
    print("pip install pillow", file=sys.stderr)
    sys.exit(1)

# Hintergrund wie MicMuted.svg (oberes Rect #765858), falls Alpha im PNG
BG = (0x76, 0x58, 0x58)


def rgb565(r: int, g: int, b: int) -> int:
    return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)


def load_rgb(path: str) -> Image.Image:
    im = Image.open(path)
    if im.mode == "RGBA":
        base = Image.new("RGB", im.size, BG)
        base.paste(im, mask=im.split()[3])
        return base
    return im.convert("RGB")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument(
        "--png",
        default=os.path.join(
            os.path.dirname(__file__), "..", "main", "assets", "mic_muted.png"
        ),
        help="Eingabe-PNG (65×98 aus MicMuted.svg)",
    )
    ap.add_argument(
        "--out",
        default=os.path.join(
            os.path.dirname(__file__), "..", "main", "mic_muted_icon.c"
        ),
        help="Ausgabe mic_muted_icon.c",
    )
    args = ap.parse_args()

    png_path = os.path.normpath(args.png)
    if not os.path.isfile(png_path):
        print(f"PNG nicht gefunden: {png_path}", file=sys.stderr)
        print(
            "Zuerst: node ESP/tools/node_raster/raster_mic_muted.cjs",
            file=sys.stderr,
        )
        sys.exit(1)

    im = load_rgb(png_path)
    w, h = im.size
    px = im.load()
    words = []
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            v = rgb565(r, g, b)
            words.append(v & 0xFF)
            words.append((v >> 8) & 0xFF)

    out_path = os.path.normpath(args.out)
    os.makedirs(os.path.dirname(out_path) or ".", exist_ok=True)
    with open(out_path, "w", encoding="utf-8") as f:
        f.write('#include "lvgl.h"\n\n')
        f.write("static const uint8_t img_mic_muted_map[] = {\n")
        line = []
        for i, b in enumerate(words):
            line.append(f"0x{b:02x}")
            if len(line) >= 16:
                f.write("    " + ", ".join(line) + ",\n")
                line = []
        if line:
            f.write("    " + ", ".join(line) + ",\n")
        f.write("};\n\n")
        f.write("const lv_image_dsc_t img_mic_muted = {\n")
        f.write("    .header = {\n")
        f.write("        .magic = LV_IMAGE_HEADER_MAGIC,\n")
        f.write("        .cf = LV_COLOR_FORMAT_RGB565,\n")
        f.write("        .flags = 0,\n")
        f.write(f"        .w = {w},\n")
        f.write(f"        .h = {h},\n")
        f.write(f"        .stride = {w * 2},\n")
        f.write("        .reserved_2 = 0,\n")
        f.write("    },\n")
        f.write("    .data_size = sizeof(img_mic_muted_map),\n")
        f.write("    .data = img_mic_muted_map,\n")
        f.write("    .reserved = NULL,\n")
        f.write("};\n")


if __name__ == "__main__":
    main()
