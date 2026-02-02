import serial
import time
import threading
import os
import struct
import spidev
from PIL import Image, ImageFile

ImageFile.LOAD_TRUNCATED_IMAGES = True

# =========================
# KONFIGURATION
# =========================
USB_PORT = '/dev/ttyGS0'
USB_BAUD = 230400

# WIEDER SICHERE GESCHWINDIGKEIT
DISP_PORT = '/dev/serial0'
DISP_BAUD = 921600

NUM_SLIDERS = 6
THRESHOLD = 5
COVER_FILE = "/tmp/cover.jpg"

state = {
    "title": "Waiting...",
    "artist": "Mixr Ready",
    "image_path": None,
    "needs_update": False
}

# =========================
# A. DISPLAY FUNKTIONEN (SCHNELL & SICHER)
# =========================
def convert_image(path):
    if not os.path.exists(path): return None
    try:
        # Bild laden
        img = Image.open(path).convert('RGB').resize((120, 120))

        pixels = list(img.getdata())
        rgb_ints = [((b & 0xF8) << 8) | ((g & 0xFC) << 3) | (r >> 3) for r, g, b in pixels]
        return struct.pack(f'>{len(rgb_ints)}H', *rgb_ints)

    except Exception as e:
        print(f"⚠️ Bildfehler: {e}")
        return None

def send_update_to_display(ser):
    print(f"Title:{state['title']}")
    try:
        # Text senden
        t_line = f"TIT:{state['title']}\n"
        a_line = f"ART:{state['artist']}\n"
        ser.write(t_line.encode('utf-8'))
        time.sleep(0.01)
        ser.write(a_line.encode('utf-8'))
        time.sleep(0.01)

        if state["image_path"]:
            raw_data = convert_image(state["image_path"])

            if raw_data:
                ser.write(b'IMG_START\n')
                # Kleiner Sicherheitsabstand für 921600 Baud
                time.sleep(0.05)
                ser.write(raw_data)
                ser.flush()
                print(f"✅ Bild gesendet.")

    except Exception as e:
        print(f"⚠️ Sende-Fehler: {e}")

# =========================
# B. WORKER THREADS
# =========================
def display_worker():
    # print("🖥️ Display Thread startet...")
    try:
        ser_disp = serial.Serial(DISP_PORT, DISP_BAUD, timeout=1)
    except Exception as e:
        print(f"⚠️ Display Port Fehler: {e}")
        return

    while True:
        if state["needs_update"]:
            send_update_to_display(ser_disp)
            state["needs_update"] = False
        time.sleep(0.05)

def pc_reader_worker():
    # print("📥 PC Reader startet...")
    ser_usb = None
    buffer = bytearray()
    JPEG_START = b'\xff\xd8'

    while True:
        if ser_usb is None:
            try:
                ser_usb = serial.Serial(USB_PORT, USB_BAUD, timeout=0.05)
                ser_usb.reset_input_buffer()
                print("✅ USB Reader Connected")
            except:
                time.sleep(2); continue

        try:
            if ser_usb.in_waiting > 0:
                chunk = ser_usb.read(ser_usb.in_waiting)
                buffer.extend(chunk)

                while True:
                    img_idx = buffer.find(b"<IMG>")
                    if img_idx != -1:
                        end_idx = buffer.find(b"<END>", img_idx)
                        if end_idx != -1:
                            raw_packet = buffer[img_idx+5 : end_idx]
                            real_start = raw_packet.find(JPEG_START)
                            if real_start != -1:
                                with open(COVER_FILE, "wb") as f:
                                    f.write(raw_packet[real_start:])
                                    f.flush()
                                    os.fsync(f.fileno())
                                state["image_path"] = COVER_FILE
                                state["needs_update"] = True
                                print("💾 Bild da.")
                            buffer = buffer[end_idx+5:]
                            continue
                        else: break

                    nl_idx = buffer.find(b"\n")
                    if nl_idx != -1:
                        if img_idx == -1 or nl_idx < img_idx:
                            raw_line = buffer[:nl_idx]
                            sp_start = raw_line.find(b"sp|")
                            if sp_start != -1:
                                clean_line = raw_line[sp_start:].decode('utf-8', errors='replace').strip()
                                p = clean_line.split("|")
                                if len(p) >= 4:
                                    if p[2] != state["title"] or p[3] != state["artist"]:
                                        state["title"] = p[2]
                                        state["artist"] = p[3]
                                        state["needs_update"] = True
                            buffer = buffer[nl_idx+1:]
                            continue
                    break
                if len(buffer) > 500000: buffer = bytearray()
            time.sleep(0.01)
        except: ser_usb = None; time.sleep(1)

def main():
    threading.Thread(target=display_worker, daemon=True).start()
    threading.Thread(target=pc_reader_worker, daemon=True).start()

    spi = spidev.SpiDev()
    try: spi.open(0, 0); spi.max_speed_hz = 1350000
    except:
        try: spi.open(0, 1); spi.max_speed_hz = 1350000
        except: pass

    def read_adc(ch):
        if ch > 7: return 0
        try:
            r = spi.xfer2([1, (8 + ch) << 4, 0])
            return ((r[1] & 3) << 8) + r[2]
        except: return 0

    print("🎛️ Mixr started...")
    prev = [-1] * NUM_SLIDERS
    last = 0

    while True:
        try:
            if time.time() - last > 0.05:
                vals = [read_adc(i) for i in range(NUM_SLIDERS)]
                if any(abs(vals[i] - prev[i]) > THRESHOLD for i in range(NUM_SLIDERS)):
                    try:
                        with open(USB_PORT, "w") as f: f.write("|".join(str(v) for v in vals) + "\n")
                    except: pass
                    prev = vals
                    last = time.time()
        except: pass
        time.sleep(0.01)

if __name__ == "__main__":
    main()