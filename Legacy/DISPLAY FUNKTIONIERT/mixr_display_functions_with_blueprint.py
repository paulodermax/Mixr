import serial
import time
import threading
import io
import sys
import os
from datetime import datetime
from PIL import Image, ImageDraw, ImageFont

# =========================
# KONFIGURATION
# =========================
# Hardware Pfade
USB_PORT = '/dev/ttyGS0'
USB_BAUD = 230400

DISP_PORT = '/dev/serial0'
DISP_BAUD = 921600

# Datei Pfade
PROJECT_DIR = "/home/paulodermax/mixr/"
BLUEPRINT_PATH = os.path.join(PROJECT_DIR, "blueprint.png")
LOG_PATH = os.path.join(PROJECT_DIR, "log.txt")

# Schriftart: Entweder System-Schrift oder eine 'font.ttf' im Mixr-Ordner
# Falls du eine eigene Font hast, ändere dies zu: os.path.join(PROJECT_DIR, "font.ttf")
FONT_PATH = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

# Globale Variablen
current_title = "Warte auf Musik..."
current_artist = "Mixr Ready"
latest_cover_data = None
update_display_event = threading.Event()

# =========================
# LOGGING
# =========================
def log(text):
    t = datetime.now().strftime("[%H:%M:%S] ")
    print(t + text)
    try:
        with open(LOG_PATH, "a") as f:
            f.write(t + text + "\n")
    except: pass
# =========================
# 1. UI COMPOSER (MIT TEXT-KÜRZUNG)
# =========================
def create_ui_image(cover_bytes):
    try:
        # --- HILFSFUNKTION ZUM KÜRZEN ---
        def truncate_text(draw, text, font, max_width):
            # Wenn der Text passt, sofort zurückgeben
            if draw.textlength(text, font=font) <= max_width:
                return text

            # Text schrittweise kürzen und "..." anhängen
            for i in range(len(text), 0, -1):
                truncated = text[:i] + "..."
                if draw.textlength(truncated, font=font) <= max_width:
                    return truncated
            return "..."
        # --------------------------------

        # A. Blueprint laden
        if os.path.exists(BLUEPRINT_PATH):
            img = Image.open(BLUEPRINT_PATH).convert("RGB")
        else:
            img = Image.new("RGB", (240, 240), "black") # Fallback

        draw = ImageDraw.Draw(img)

        # B. Cover einfügen (120x120)
        if cover_bytes:
            try:
                cover = Image.open(io.BytesIO(cover_bytes)).convert("RGB")
                cover = cover.resize((120, 120))
                # Position: Mittig (X=60), Y=40
                img.paste(cover, (60, 40))
            except Exception as e:
                log(f"Cover Fehler: {e}")

        # C. Text rendern
        try:
            # Schrift laden
            font_title = ImageFont.truetype(FONT_PATH, 18)
            font_artist = ImageFont.truetype(FONT_PATH, 14)

            # Farben
            text_color = (255, 255, 255)
            artist_color = (180, 180, 180)

            # --- HIER IST DIE ÄNDERUNG ---
            # Wir definieren eine maximale Breite (z.B. 230px, damit links/rechts 5px Platz bleiben)
            MAX_WIDTH = 180
            MAX_WIDTH_ARTIST =120

            safe_title = truncate_text(draw, current_title, font_title, MAX_WIDTH)
            safe_artist = truncate_text(draw, current_artist, font_artist, MAX_WIDTH_ARTIST)

            # Jetzt können wir weiterhin "mm" (Mitte) nutzen, da der Text garantiert passt!
            draw.text((120, 175), safe_title, font=font_title, fill=text_color, anchor="mm")
            draw.text((120, 200), safe_artist, font=font_artist, fill=artist_color, anchor="mm")
            # -----------------------------

        except Exception as e:
            log(f"Font Fehler: {e}")

        return img

    except Exception as e:
        log(f"UI Composer Fehler: {e}")
        return Image.new("RGB", (240, 240), "red")

# =========================
# 2. RGB565 CONVERTER (TURBO)
# =========================
def image_to_rgb565_fast(img):
    if img.mode != 'RGB':
        img = img.convert('RGB')

    # Sicherstellen, dass es 240x240 ist
    if img.size != (240, 240):
        img = img.resize((240, 240))

    pixels = list(img.getdata())
    buf = bytearray(len(pixels) * 2)
    idx = 0

    for r, g, b in pixels:
        val = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)
        buf[idx] = (val >> 8) & 0xFF
        buf[idx+1] = val & 0xFF
        idx += 2
    return buf

# =========================
# 3. DISPLAY WORKER THREAD
# =========================
def display_worker():
    log("🖥️ Display-Worker gestartet...")
    try:
        # write_timeout verhindert, dass er hängt, wenn das Display spinnt
        ser = serial.Serial(DISP_PORT, DISP_BAUD, timeout=1, write_timeout=2)
    except Exception as e:
        log(f"❌ Display UART Fehler: {e}")
        return

    while True:
        update_display_event.wait()
        update_display_event.clear()

        try:
            # UI bauen
            final_img = create_ui_image(latest_cover_data)

            # Konvertieren
            raw_bytes = image_to_rgb565_fast(final_img)

            # Senden (Header 'B' + Daten)
            if len(raw_bytes) == 240 * 240 * 2:
                full_packet = b'B' + raw_bytes
                ser.write(full_packet)
                ser.flush()
                # log("✅ Display Update OK") # Einkommentieren für Debugging
        except Exception as e:
            log(f"Display Sende-Fehler: {e}")

# =========================
# 4. MAIN (USB LISTENER)
# =========================
def main():
    global current_title, current_artist, latest_cover_data

    # Display Thread starten
    t = threading.Thread(target=display_worker, daemon=True)
    t.start()

    log(f"📡 Starte Listener im Ordner {PROJECT_DIR}")

    try:
        ser = serial.Serial(USB_PORT, USB_BAUD, timeout=0.05)
        ser.reset_input_buffer()
    except Exception as e:
        log(f"❌ USB Fehler: {e}")
        return

    buffer = bytearray()
    in_image = False
    image_buffer = bytearray()

    while True:
        try:
            if ser.in_waiting > 0:
                chunk = ser.read(ser.in_waiting)
                buffer.extend(chunk)
            elif len(buffer) == 0:
                time.sleep(0.01)
                continue

            while True:
                # --- MODUS: BILD EMPFANGEN ---
                if in_image:
                    end_idx = buffer.find(b"<END>")
                    if end_idx != -1:
                        image_buffer.extend(buffer[:end_idx])

                        # Bild speichern & UI Update triggern
                        latest_cover_data = bytes(image_buffer)
                        update_display_event.set()

                        # Windows Bestätigung senden
                        ser.write(f"IMG_OK|{len(image_buffer)}\n".encode())

                        # Reset
                        in_image = False
                        image_buffer = bytearray()
                        buffer = buffer[end_idx+5:] # +5 für <END>
                        continue
                    else:
                        image_buffer.extend(buffer)
                        buffer = bytearray()
                        break

                # --- MODUS: TEXT / HEADER SUCHEN ---
                else:
                    img_idx = buffer.find(b"<IMG>")
                    nl_idx = buffer.find(b"\n")

                    # Fall A: Bild beginnt
                    if img_idx != -1 and (nl_idx == -1 or img_idx < nl_idx):
                        log("⬇️ Bild-Empfang...")
                        in_image = True
                        buffer = buffer[img_idx+5:] # +5 für <IMG>
                        continue

                    # Fall B: Textzeile
                    elif nl_idx != -1:
                        line = buffer[:nl_idx].decode(errors='ignore').strip()

                        if line.startswith("sp|"):
                            parts = line.split("|")
                            if len(parts) >= 4:
                                current_title = parts[2]
                                current_artist = parts[3]
                                log(f"🎵 {current_title}")
                                ser.write(f"TXT_OK|{parts[2]}\n".encode())
                                # Optional: Display updaten auch wenn noch kein Bild da ist (zeigt nur Text)
                                # update_display_event.set()

                        elif line.startswith("TIME|"):
                             # Hier könntest du die Zeit setzen wenn du willst
                             pass

                        buffer = buffer[nl_idx+1:]
                        continue

                    else:
                        break # Warten auf mehr Daten

        except KeyboardInterrupt:
            break
        except Exception as e:
            log(f"Hauptfehler: {e}")
            time.sleep(1)

if __name__ == "__main__":
    main()