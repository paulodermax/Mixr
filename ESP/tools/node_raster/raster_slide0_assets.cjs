/**
 * Section1 (Slide 0): 240×508 oben aus 240×536 (Carousel-Höhe).
 * MicMuted 65×98, HeadphonesMuted 96×102.
 */
const sharp = require("sharp");
const path = require("path");

const root = path.join(__dirname, "..", "..");
const assets = path.join(root, "main", "assets");

const section1 = path.join(assets, "Section1.svg");
const mic = path.join(assets, "MicMuted.svg");
const hp = path.join(assets, "HeadphonesMuted.svg");

const outSlide = path.join(assets, "slide1_section_bg.png");
const outMic = path.join(assets, "mic_muted.png");
const outHp = path.join(assets, "headphones_deafened.png");

async function main() {
  await sharp(section1)
    .resize(240, 536)
    .extract({ left: 0, top: 0, width: 240, height: 508 })
    .png()
    .toFile(outSlide);
  console.log("wrote", outSlide);

  await sharp(mic).resize(65, 98).png().toFile(outMic);
  console.log("wrote", outMic);

  await sharp(hp).resize(96, 102).png().toFile(outHp);
  console.log("wrote", outHp);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
