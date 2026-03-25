#!/usr/bin/env python3
"""Ersetzt img_slide1_bg in ui_assets.c aus slide1_section_bg.png (RGB565)."""
import os
import sys

try:
    from PIL import Image
except ImportError:
    print("pip install pillow", file=sys.stderr)
    sys.exit(1)


def rgb565(r: int, g: int, b: int) -> int:
    return ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)


def load_rgb(path: str) -> Image.Image:
    im = Image.open(path)
    return im.convert("RGB")


def main() -> None:
    root = os.path.join(os.path.dirname(__file__), "..", "main")
    png = os.path.normpath(os.path.join(root, "assets", "slide1_section_bg.png"))
    c_path = os.path.normpath(os.path.join(root, "ui_assets.c"))

    if not os.path.isfile(png):
        print(f"Fehlt: {png}", file=sys.stderr)
        sys.exit(1)

    im = load_rgb(png)
    w, h = im.size
    px = im.load()
    words = []
    for y in range(h):
        for x in range(w):
            r, g, b = px[x, y]
            v = rgb565(r, g, b)
            words.append(v & 0xFF)
            words.append((v >> 8) & 0xFF)

    lines_out = []
    line = []
    for b in words:
        line.append(f"0x{b:02x}")
        if len(line) >= 16:
            lines_out.append("    " + ", ".join(line) + ",\n")
            line = []
    if line:
        lines_out.append("    " + ", ".join(line) + ",\n")

    new_block = (
        "static const uint8_t img_slide1_bg_map[] = {\n"
        + "".join(lines_out)
        + "};\n\n"
        "const lv_image_dsc_t img_slide1_bg = {\n"
        "    .header = {\n"
        "        .magic = LV_IMAGE_HEADER_MAGIC,\n"
        "        .cf = LV_COLOR_FORMAT_RGB565,\n"
        "        .flags = 0,\n"
        f"        .w = {w},\n"
        f"        .h = {h},\n"
        f"        .stride = {w * 2},\n"
        "        .reserved_2 = 0,\n"
        "    },\n"
        "    .data_size = sizeof(img_slide1_bg_map),\n"
        "    .data = img_slide1_bg_map,\n"
        "    .reserved = NULL,\n"
        "};\n\n"
    )

    with open(c_path, "r", encoding="utf-8") as f:
        s = f.read()

    start = s.find("static const uint8_t img_slide1_bg_map[] = {")
    end = s.find("static const uint8_t img_slide2_bg_map[] = {")
    if start == -1 or end == -1:
        print("Marker in ui_assets.c nicht gefunden.", file=sys.stderr)
        sys.exit(1)

    s2 = s[:start] + new_block + s[end:]
    with open(c_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(s2)
    print(f"ui_assets.c: img_slide1_bg ersetzt ({w}×{h}).")


if __name__ == "__main__":
    main()
