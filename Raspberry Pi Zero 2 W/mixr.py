import serial
import time
import threading
import spidev

# ==============================================================================
# CONFIGURATION
# ==============================================================================
USB_PORT = '/dev/ttyGS0'
USB_BAUD = 230400

DISP_PORT = '/dev/serial0'
DISP_BAUD = 921600

NUM_SLIDERS = 6
THRESHOLD = 5
IMG_SIZE = 28800  # 120x120 RGB565 (2 Bytes pro Pixel)

state = {
    "title": "Waiting...",
    "artist": "Mixr Ready",
    "progress": 0.0,
    "image_data": None,
    "needs_update": False
}

state_lock = threading.Lock()

# ==============================================================================
# DISPLAY WORKER (Pi -> RP2350)
# ==============================================================================
def display_worker():
    try:
        # Timeout reduziert, damit er nicht blockiert
        ser_disp = serial.Serial(DISP_PORT, DISP_BAUD, timeout=0.1)
    except Exception as e:
        print(f"[ERROR] Display Port: {e}")
        return

    while True:
        update_needed = False
        with state_lock:
            if state["needs_update"]:
                t = state["title"]
                a = state["artist"]
                p = state["progress"]
                img = state["image_data"]

                state["needs_update"] = False
                update_needed = True

        if update_needed:
            try:
                # 1. Metadaten senden
                ser_disp.write(f"TIT:{t}\n".encode('utf-8'))
                ser_disp.write(f"ART:{a}\n".encode('utf-8'))
                ser_disp.write(f"PRO:{p:.3f}\n".encode('utf-8'))

                # 2. Bild senden (Chunked für Stabilität ohne Rauschen)
                if img and len(img) == IMG_SIZE:
                    ser_disp.write(b'IMG_START\n')
                    ser_disp.flush()

                    time.sleep(0.01) # Synchronisations-Pause für RP2350

                    # WICHTIG: Chunked Write! 
                    # Verhindert Puffer-Überläufe und Rauschen auf der SPI/UART Leitung
                    chunk_size = 2048
                    for i in range(0, len(img), chunk_size):
                        ser_disp.write(img[i:i+chunk_size])
                        ser_disp.flush()
                        # Minimale Pause, damit der RP2350 mit dem Lesen nachkommt
                        time.sleep(0.001)

                    with state_lock:
                        state["image_data"] = None # Bild nach dem Senden verwerfen

            except Exception as e:
                print(f"[ERROR] TX Display: {e}")

        time.sleep(0.02)
# ==============================================================================
# PC READER WORKER (PC -> Pi)
# ==============================================================================
def pc_reader_worker():
    ser_usb = None
    buffer = bytearray()

    while True:
        if ser_usb is None:
            try:
                ser_usb = serial.Serial(USB_PORT, USB_BAUD, timeout=0.05)
                ser_usb.reset_input_buffer()
                print("[INFO] USB Reader Connected")
            except:
                time.sleep(2)
                continue

        try:
            if ser_usb.in_waiting > 0:
                chunk = ser_usb.read(ser_usb.in_waiting)
                buffer.extend(chunk)

                # Puffer verarbeiten
                while True:
                    # A) Bilddaten parsen (<IMG> ... <END>)
                    img_idx = buffer.find(b"<IMG>")
                    if img_idx != -1:
                        # Wir suchen nicht nach <END> in Binärdaten (Kollisionsgefahr),
                        # sondern lesen exakt IMG_SIZE Bytes nach dem <IMG>-Tag.
                        start_data = img_idx + 5
                        if len(buffer) >= start_data + IMG_SIZE:
                            raw_img = buffer[start_data : start_data + IMG_SIZE]

                            with state_lock:
                                state["image_data"] = raw_img
                                state["needs_update"] = True

                            # Puffer aufräumen (inklusive eines eventuellen <END> Tags)
                            end_idx = buffer.find(b"<END>", start_data + IMG_SIZE)
                            if end_idx != -1:
                                buffer = buffer[end_idx + 5:]
                            else:
                                buffer = buffer[start_data + IMG_SIZE:]
                            continue
                        else:
                            break # Warten auf mehr Daten

                    # B) Textdaten parsen (Zeilenumbruch)
                    nl_idx = buffer.find(b"\n")
                    if nl_idx != -1:
                        if img_idx == -1 or nl_idx < img_idx:
                            raw_line = buffer[:nl_idx]

                            # Legacy Support: sp| Command
                            sp_start = raw_line.find(b"sp|")
                            if sp_start != -1:
                                clean = raw_line[sp_start:].decode('utf-8', errors='ignore').strip()
                                p = clean.split("|")
                                if len(p) >= 4:
                                    with state_lock:
                                        if p[2] != state["title"] or p[3] != state["artist"]:
                                            state["title"] = p[2]
                                            state["artist"] = p[3]
                                            state["needs_update"] = True

                            # Neuer Support: Direkte Befehle vom PC
                            else:
                                try:
                                    msg = raw_line.decode('utf-8', errors='ignore').strip()
                                    with state_lock:
                                        if msg.startswith("PRO:"):
                                            state["progress"] = float(msg[4:])
                                            state["needs_update"] = True
                                        elif msg.startswith("TIT:"):
                                            state["title"] = msg[4:]
                                            state["needs_update"] = True
                                        elif msg.startswith("ART:"):
                                            state["artist"] = msg[4:]
                                            state["needs_update"] = True
                                except:
                                    pass

                            buffer = buffer[nl_idx + 1:]
                            continue

                    break # Schleife beenden, auf nächste Chunks warten

                # Puffer-Überlaufschutz
                if len(buffer) > 100_000:
                    buffer.clear()

            time.sleep(0.005)

        except Exception as e:
            print(f"[ERROR] USB Read: {e}")
            ser_usb = None
            time.sleep(1)

# ==============================================================================
# MAIN (ADC Loop)
# ==============================================================================
def main():
    threading.Thread(target=display_worker, daemon=True).start()
    threading.Thread(target=pc_reader_worker, daemon=True).start()

    spi = spidev.SpiDev()
    try:
        spi.open(0, 0)
        spi.max_speed_hz = 1350000
    except:
        try:
            spi.open(0, 1)
            spi.max_speed_hz = 1350000
        except Exception as e:
            print(f"[ERROR] SPI Init: {e}")
            return

    def read_adc(ch):
        if ch > 7: return 0
        try:
            r = spi.xfer2([1, (8 + ch) << 4, 0])
            return ((r[1] & 3) << 8) + r[2]
        except:
            return 0

    print("[INFO] Mixr Pipeline Router Online.")
    prev = [-1] * NUM_SLIDERS
    last = 0

    while True:
        try:
            now = time.time()
            if now - last > 0.05:
                vals = [read_adc(i) for i in range(NUM_SLIDERS)]
                if any(abs(vals[i] - prev[i]) > THRESHOLD for i in range(NUM_SLIDERS)):
                    try:
                        with open(USB_PORT, "w") as f:
                            f.write("|".join(str(v) for v in vals) + "\n")
                    except:
                        pass
                    prev = vals
                    last = now
        except Exception:
            pass

        time.sleep(0.01)

if __name__ == "__main__":
    main()