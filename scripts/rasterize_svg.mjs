/**
 * Rasterize SVG files to PNG (fixed size). Uses sharp (Windows-friendly).
 * ClockIndicator.svg: beide Pfeile in einer Datei → linke Spalte: oben=up, unten=down.
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

const jobs = [
  { file: "Brightness.svg", out: "brightness.png", size: 110 },
  { file: "Hardware.svg", out: "hardware.png", size: 110 },
  { file: "Debug.svg", out: "debug.png", size: 110 },
  { file: "Restart.svg", out: "restart.png", size: 110 },
  { file: "Bell.svg", out: "bell.png", size: 112 },
  { file: "Mic.svg", out: "mic.png", size: 72 },
  { file: "Headphones.svg", out: "headphones.png", size: 72 },
];

const CLOCK_OUT_PX = 32;

async function rasterizeClockSplit(svgPath, destDir) {
  const buf = fs.readFileSync(svgPath);
  const baseW = 168;
  const baseH = 186;
  const scaledW = 256;
  const scaledH = Math.round((scaledW * baseH) / baseW);
  const resized = await sharp(buf)
    .resize(scaledW, scaledH, { fit: "fill" })
    .ensureAlpha()
    .png()
    .toBuffer();
  const halfW = Math.floor(scaledW / 2);
  const halfH = Math.floor(scaledH / 2);
  const bottomH = scaledH - halfH;
  await sharp(resized)
    .extract({ left: 0, top: 0, width: halfW, height: halfH })
    .resize(CLOCK_OUT_PX, CLOCK_OUT_PX, {
      fit: "contain",
      background: { r: 0, g: 0, b: 0, alpha: 0 },
    })
    .ensureAlpha()
    .png()
    .toFile(path.join(destDir, "clock_indicator_up.png"));
  await sharp(resized)
    .extract({ left: 0, top: halfH, width: halfW, height: bottomH })
    .resize(CLOCK_OUT_PX, CLOCK_OUT_PX, {
      fit: "contain",
      background: { r: 0, g: 0, b: 0, alpha: 0 },
    })
    .ensureAlpha()
    .png()
    .toFile(path.join(destDir, "clock_indicator_down.png"));
  console.log("clock_indicator_up.png", CLOCK_OUT_PX, "(linke Spalte, oben)");
  console.log("clock_indicator_down.png", CLOCK_OUT_PX, "(linke Spalte, unten)");
}

fs.mkdirSync(outDir, { recursive: true });

for (const j of jobs) {
  const p = path.join(root, j.file);
  if (!fs.existsSync(p)) {
    console.error("Missing:", p);
    process.exit(1);
  }
  const buf = fs.readFileSync(p);
  await sharp(buf)
    .resize(j.size, j.size, { fit: "contain", background: { r: 0, g: 0, b: 0, alpha: 0 } })
    .ensureAlpha()
    .png()
    .toFile(path.join(outDir, j.out));
  console.log(j.out, j.size);
}

const clockP = path.join(root, "ClockIndicator.svg");
if (!fs.existsSync(clockP)) {
  console.error("Missing:", clockP);
  process.exit(1);
}
await rasterizeClockSplit(clockP, outDir);
