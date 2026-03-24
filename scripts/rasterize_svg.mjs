/**
 * Carousel-Hintergründe: Slide1.svg … Slide4.svg (240×536) → slide1_bg.png … slide4_bg.png.
 * Untere 28px weglassen (Pager separat), Ausgabe 240×508.
 */
import fs from "fs";
import path from "path";
import sharp from "sharp";

const root = process.argv[2];
const outDir = process.argv[3];
if (!root || !outDir) {
  console.error("Usage: node rasterize_svg.mjs <svg_dir> <png_out_dir>");
  process.exit(1);
}

const TFT_W = 240;
const SRC_H = 536;
const CAROUSEL_H = 508;

fs.mkdirSync(outDir, { recursive: true });

async function rasterSlide(name, outFile) {
  const p = path.join(root, `${name}.svg`);
  if (!fs.existsSync(p)) {
    console.error("Missing:", p);
    process.exit(1);
  }
  const buf = fs.readFileSync(p);
  await sharp(buf)
    .resize(TFT_W, SRC_H, { fit: "fill" })
    .extract({ left: 0, top: 0, width: TFT_W, height: CAROUSEL_H })
    .ensureAlpha()
    .png()
    .toFile(path.join(outDir, outFile));
  console.log(outFile, TFT_W, CAROUSEL_H);
}

await rasterSlide("Slide1", "slide1_bg.png");
await rasterSlide("Slide2", "slide2_bg.png");
await rasterSlide("Slide3", "slide3_bg.png");
await rasterSlide("Slide4", "slide4_bg.png");

/** Focus-Slide: Pfeil-Stack (74×181) → schmal neben der Uhr */
async function rasterClockArrows(name, outFile, w) {
  const p = path.join(root, `${name}.svg`);
  if (!fs.existsSync(p)) {
    console.error("Missing:", p);
    process.exit(1);
  }
  const buf = fs.readFileSync(p);
  await sharp(buf)
    .resize(w, null, { fit: "inside" })
    .ensureAlpha()
    .png()
    .toFile(path.join(outDir, outFile));
  console.log(outFile, "w=", w);
}

await rasterClockArrows("ClockUpDownSelected", "clock_updown_selected.png", 56);
await rasterClockArrows("ClockUpDownUnselected", "clock_updown_unselected.png", 56);
