/**
 * Rasterisiert MicMuted.svg → mic_muted.png (72×71) für gen_mic_muted_icon.py.
 * Voraussetzung: npm install (sharp) in diesem Ordner.
 */
const sharp = require("sharp");
const path = require("path");

const root = path.join(__dirname, "..", "..");
const svg = path.join(root, "main", "assets", "MicMuted.svg");
const png = path.join(root, "main", "assets", "mic_muted.png");

sharp(svg)
  .resize(72, 71)
  .png()
  .toFile(png)
  .then(() => console.log("wrote", png))
  .catch((e) => {
    console.error(e);
    process.exit(1);
  });
