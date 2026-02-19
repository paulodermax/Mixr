from machine import Pin, SPI, UART, I2C
import time
import math
import framebuf  # <--- Das hier hat gefehlt!
from gc9a01 import GC9A01
from touch_driver import Touch_CST816S

# --- CONFIGURATION ---
SPI_SPEED = 100_000_000 
DC, CS, SCK, MOSI, RST, BL = 8, 9, 10, 11, 13, 25
SDA, SCL = 6, 7

# --- INITIALIZATION ---
# Display
spi = SPI(1, baudrate=SPI_SPEED, polarity=0, phase=0, sck=Pin(SCK), mosi=Pin(MOSI))
display = GC9A01(spi, DC, CS, RST, BL)

# Touch
i2c = I2C(1, scl=Pin(SCL), sda=Pin(SDA), freq=400_000)
touch = Touch_CST816S(i2c)

# UART
uart = UART(0, baudrate=921600, tx=Pin(16), rx=Pin(17), rxbuf=4096)

# Global state
current_title = "Waiting..."
current_artist = "Mixr Ready"
current_progress = 0.0
img_buffer = bytearray(120 * 120 * 2)
img_fb = framebuf.FrameBuffer(img_buffer, 120, 120, framebuf.RGB565)

@micropython.native
def render_ui():
    fb = display.fb
    fb.fill(0x0000) # Puffer leeren
    
    # 1. Das Cover-Bild in die Mitte kopieren (Position 60, 60)
    # Das passiert im RAM, das Display sieht davon noch nichts!
    fb.blit(img_fb, 60, 60)
    
    # 2. Den Progress-Ring zeichnen (über oder um das Bild)
    r, cx, cy = 118, 120, 120
    end_angle = int(current_progress * 360) - 90
    for a in range(-90, end_angle, 2):
        rad = math.radians(a)
        px, py = int(cx + r * math.cos(rad)), int(cy + r * math.sin(rad))
        rad_next = math.radians(a + 2)
        px2, py2 = int(cx + r * math.cos(rad_next)), int(cy + r * math.sin(rad_next))
        fb.line(px, py, px2, py2, 0xF800)
        fb.pixel(px+1, py, 0xF800)
    
    # 3. Texte schreiben
    fb.text(current_title[:20], (240 - len(current_title[:20])*8) // 2, 40, 0xFFFF)
    fb.text(current_artist[:20], (240 - len(current_artist[:20])*8) // 2, 190, 0x07E0)
    
    # 4. Jetzt alles zusammen ans Display schicken
    display.show()

# --- OPTIMIERTER UART CHECK FÜR RP2350 ---
def check_uart():
    global current_title, current_artist, current_progress
    if uart.any():
        line = uart.readline()
        if not line: return False
        
        try:
            # Bild-Streaming hat höchste Priorität
            if b'IMG_START' in line:
                load_image_stream()
                return True
            
            msg = line.decode().strip()
            if msg.startswith("TIT:"): 
                current_title = msg[4:]
                return True
            elif msg.startswith("ART:"): 
                current_artist = msg[4:]
                return True
            elif msg.startswith("PRO:"): 
                current_progress = float(msg[4:])
                return True
        except: 
            pass
    return False

def load_image_stream():
    global img_buffer
    expected = 28800
    read_count = 0
    print("DEBUG: IMG_START erkannt, lese Daten...")
    
    start_time = time.ticks_ms()
    while read_count < expected:
        # 2 Sekunden Timeout
        if time.ticks_diff(time.ticks_ms(), start_time) > 2000:
            print(f"TIMEOUT: Nur {read_count}/{expected} erhalten")
            return False
            
        if uart.any():
            # Direkt in das Bytearray schreiben (schnellster Weg)
            chunk = uart.read(min(uart.any(), expected - read_count))
            if chunk:
                img_buffer[read_count:read_count+len(chunk)] = chunk
                read_count += len(chunk)
                
    print(f"✅ Bild fertig: {read_count} Bytes empfangen.")
    return True

# --- START ---
# --- START ---
print("🚀 Launching Mixr Master Engine...")
render_ui()

while True:
    data_received = False
    
    # UART "leerfressen": Wir verarbeiten alle anstehenden Nachrichten
    while uart.any():
        if check_uart():
            data_received = True
        # Kurze Pause, falls das nächste Paket (z.B. das Bild) noch im Flug ist
        time.sleep_ms(10) 
    
    # Erst wenn alle Daten verarbeitet wurden, zeichnen wir genau EINMAL
    if data_received:
        render_ui()
    
    # 2. Touch Check (unabhängig vom UART)
    touch_data = touch.get_touch()
    if touch_data:
        gesture, x, y = touch_data
        if gesture != "NONE":
            print(f"Geste: {gesture}")
            # Optional: uart.write(f"CMD:{gesture}\n")

    time.sleep(0.01)
