#!/usr/bin/env python3
"""PNG -> LVGL 9 lv_image_dsc_t (RGB565) C source."""

from __future__ import annotations

import argparse
import os
import struct
import sys

try:
    from PIL import Image
except ImportError:
    print("Install Pillow: pip install Pillow", file=sys.stderr)
    sys.exit(1)

# MIXR slide background #765858 (same as MIXR_COLOR_BG in ui_mixr.cpp)
BG_R, BG_G, BG_B = 0x76, 0x58, 0x58

LV_IMAGE_HEADER_MAGIC = 0x19
LV_COLOR_FORMAT_RGB565 = 0x12


def rgb888_to_rgb565_u16(r: int, g: int, b: int) -> int:
    return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)


def blend_over_bg(r: int, g: int, b: int, a: int) -> tuple[int, int, int]:
    if a >= 255:
        return r, g, b
    if a <= 0:
        return BG_R, BG_G, BG_B
    af = a / 255.0
    return (
        int(round(r * af + BG_R * (1.0 - af))),
        int(round(g * af + BG_G * (1.0 - af))),
        int(round(b * af + BG_B * (1.0 - af))),
    )


def png_to_rgb565_bytes(path: str) -> tuple[int, int, bytes]:
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    px = im.load()
    out = bytearray()
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            br, bg, bb = blend_over_bg(r, g, b, a)
            u16 = rgb888_to_rgb565_u16(br, bg, bb)
            out += struct.pack("<H", u16)
    stride = w * 2
    return w, h, bytes(out)


def emit_c(
    assets: list[tuple[str, str, str]],
    out_c: str,
    out_h: str,
) -> None:
    lines_h = [
        "#pragma once",
        "",
        '#include "lvgl.h"',
        "",
        "#ifdef __cplusplus",
        "extern \"C\" {",
        "#endif",
        "",
    ]
    lines_c = [
        '#include "ui_assets.h"',
        "",
    ]

    for sym, _png, c_name in assets:
        lines_h.append(f"LV_IMAGE_DECLARE({sym});")
        lines_h.append("")

    lines_h.extend(["#ifdef __cplusplus", "}", "#endif", ""])

    for sym, _png, c_name in assets:
        w, h, data = png_to_rgb565_bytes(c_name)
        stride = w * 2
        chunks = [f"0x{b:02x}" for b in data]
        lines = []
        for i in range(0, len(chunks), 16):
            lines.append("    " + ", ".join(chunks[i : i + 16]) + ("," if i + 16 < len(chunks) else ""))
        body = "\n".join(lines)
        lines_c.append(f"static const uint8_t {sym}_map[] = {{\n{body}\n}};")
        lines_c.append("")
        lines_c.append(f"const lv_image_dsc_t {sym} = {{")
        lines_c.append("    .header = {")
        lines_c.append(f"        .magic = LV_IMAGE_HEADER_MAGIC,")
        lines_c.append(f"        .cf = LV_COLOR_FORMAT_RGB565,")
        lines_c.append("        .flags = 0,")
        lines_c.append(f"        .w = {w},")
        lines_c.append(f"        .h = {h},")
        lines_c.append(f"        .stride = {stride},")
        lines_c.append("        .reserved_2 = 0,")
        lines_c.append("    },")
        lines_c.append(f"    .data_size = sizeof({sym}_map),")
        lines_c.append(f"    .data = {sym}_map,")
        lines_c.append("    .reserved = NULL,")
        lines_c.append("};")
        lines_c.append("")

    os.makedirs(os.path.dirname(out_c) or ".", exist_ok=True)
    with open(out_h, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines_h) + "\n")
    with open(out_c, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines_c) + "\n")


def main() -> None:
    p = argparse.ArgumentParser()
    p.add_argument("--png-dir", required=True)
    p.add_argument("--out-c", required=True)
    p.add_argument("--out-h", required=True)
    args = p.parse_args()

    # symbol name -> png filename
    mapping = [
        ("img_slide1_bg", "slide1_bg.png"),
        ("img_slide2_bg", "slide2_bg.png"),
        ("img_slide3_bg", "slide3_bg.png"),
        ("img_slide4_bg", "slide4_bg.png"),
        ("img_clock_updown_selected", "clock_updown_selected.png"),
        ("img_clock_updown_unselected", "clock_updown_unselected.png"),
    ]
    assets = []
    for sym, name in mapping:
        path = os.path.join(args.png_dir, name)
        if not os.path.isfile(path):
            print("Missing", path, file=sys.stderr)
            sys.exit(1)
        assets.append((sym, name, path))

    emit_c(assets, args.out_c, args.out_h)
    print("Wrote", args.out_c, args.out_h)


if __name__ == "__main__":
    main()
