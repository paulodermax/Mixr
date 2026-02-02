import serial
from datetime import datetime

ser = serial.Serial('/dev/ttyGS0', 230400, timeout=1)

stream_buffer = bytearray()
image_buffer = bytearray()
in_image = False

log_path = "/home/paulodermax/Desktop/log.txt"

def log(text):
    timestamp = datetime.now().strftime("[%Y-%m-%d %H:%M:%S] ")
    with open(log_path, "a") as f:
        f.write(timestamp + text + "\n")

print("📡 Starte Serial-Listener...")

while True:
    try:
        chunk = ser.read(256)
        if not chunk:
            continue

        stream_buffer.extend(chunk)

        # Textnachricht verarbeiten
        if not in_image and b"sp|" in stream_buffer:
            try:
                start = stream_buffer.find(b"sp|")
                end = stream_buffer.find(b"\n", start)
                if end != -1:
                    line = stream_buffer[start:end].decode(errors="ignore")
                    parts = line.split("|")
                    if len(parts) >= 4:
                        title = parts[2]
                        artist = parts[3]
                        print(f"\n🎵 Titel: {title}")
                        print(f"🎤 Künstler: {artist}")
                        log(f"Titel: {title} | Künstler: {artist}")
                        ser.write(f"TXT_OK|{title}\n".encode())
                    stream_buffer = stream_buffer[end + 1:]
            except Exception as e:
                log(f"⚠️ Fehler beim Parsen von Text: {e}")

        # Bildübertragung starten
        if not in_image and b"<IMG>" in stream_buffer:
            idx = stream_buffer.find(b"<IMG>") + len(b"<IMG>")
            in_image = True
            image_buffer = bytearray()
            image_buffer.extend(stream_buffer[idx:])
            stream_buffer = bytearray()

        # Bildübertragung beenden
        elif in_image and b"<END>" in stream_buffer:
            idx = stream_buffer.find(b"<END>")
            image_buffer.extend(stream_buffer[:idx])
            with open("cover.jpg", "wb") as f:
                f.write(image_buffer)

            log(f"📸 Bild empfangen ({len(image_buffer)} Bytes)")
            ser.write(f"IMG_OK|{len(image_buffer)}\n".encode())

            in_image = False
            image_buffer.clear()
            stream_buffer = stream_buffer[idx + len(b"<END>"):]
        elif in_image:
            image_buffer.extend(stream_buffer)
            stream_buffer = bytearray()

    except Exception as e:
        log(f"❌ Fehler: {e}")
