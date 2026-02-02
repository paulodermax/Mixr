import serial
import time
from PIL import Image
import struct
import os

UART_PORT = '/dev/serial0'
BAUD_RATE = 921600

def convert_image(path):
    """Konvertiert Bild zu RGB565 Bytes"""
    if not os.path.exists(path): return None
    img = Image.open(path).convert('RGB').resize((120, 120))
    pixels = list(img.getdata())
    buffer = bytearray()
    for r, g, b in pixels:
        rgb = ((r & 0xF8) << 8) | ((g & 0xFC) << 3) | (b >> 3)
        buffer.extend(struct.pack('>H', rgb))
    return buffer

def send_update(ser, title, artist, image_path):
    print(f"Update: {title} - {artist}")

    # 1. Text senden
    ser.write(f"TIT:{title}\n".encode('utf-8'))
    time.sleep(0.01)
    ser.write(f"ART:{artist}\n".encode('utf-8'))
    time.sleep(0.01)

    # 2. Bild senden
    raw_img = convert_image(image_path)
    if raw_img:
        ser.write(b'IMG_START\n')
        time.sleep(0.02) # Sync Pause
        ser.write(raw_img)
        ser.flush()

    print("Daten gesendet.")

def main():
    ser = serial.Serial(UART_PORT, BAUD_RATE, timeout=1)

    # Playlist Simulation
    playlist = [
        {"t": "Bohemian Rhapsody", "a": "Queen", "img": "/home/paulodermax/cover.jpg"},
        {"t": "Blinding Lights", "a": "The Weeknd", "img": "/home/paulodermax/cover.jpg"},
        {"t": "Stairway to Heaven - Remastered", "a": "Led Zeppelin", "img": "/home/paulodermax/cover.jpg"}
    ]

    idx = 0
    try:
        while True:
            track = playlist[idx]
            send_update(ser, track["t"], track["a"], track["img"])

            # Warte 10 Sekunden (Simulierte Songlänge)
            time.sleep(10)

            idx = (idx + 1) % len(playlist)

    except KeyboardInterrupt:
        pass
    finally:
        ser.close()

if __name__ == "__main__":
    main()