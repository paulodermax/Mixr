from machine import Pin, I2C, SPI, PWM, UART
import framebuf
import time
import gc

# --- KONFIGURATION ---
BAUD_RATE = 921600
IMG_W = 120
IMG_H = 120
IMG_SIZE = IMG_W * IMG_H * 2
SCREEN_W = 240
SCREEN_H = 240
SCREEN_SIZE = SCREEN_W * SCREEN_H * 2

# Speicher sofort bereinigen
gc.collect()

# --- DISPLAY TREIBER ---
Vbat_Pin = 29
BL = 25
DC = 8
CS = 9
SCK = 10
MOSI = 11
RST = 13

class LCD_1inch28(framebuf.FrameBuffer):
    def __init__(self):
        self.width = 240
        self.height = 240
        self.cs = Pin(CS, Pin.OUT)
        self.rst = Pin(RST, Pin.OUT)
        self.cs(1)
        self.spi = SPI(1, 100_000_000, polarity=0, phase=0, bits=8, sck=Pin(SCK), mosi=Pin(MOSI), miso=None)
        self.dc = Pin(DC, Pin.OUT)
        self.dc(1)
        
        # Puffer 1: Der AKTUELLE Screen (115 KB)
        self.buffer = bytearray(self.height * self.width * 2)
        super().__init__(self.buffer, self.width, self.height, framebuf.RGB565)
        self.init_display()
        
        self.pwm = PWM(Pin(BL))
        self.pwm.freq(5000)
        self.pwm.duty_u16(65535)

    def write_cmd(self, cmd):
        self.cs(1); self.dc(0); self.cs(0)
        self.spi.write(bytearray([cmd]))
        self.cs(1)

    def write_data(self, buf):
        self.cs(1); self.dc(1); self.cs(0)
        self.spi.write(bytearray([buf]))
        self.cs(1)

    def setWindows(self, Xstart, Ystart, Xend, Yend): 
        self.write_cmd(0x2A); self.write_data(0x00); self.write_data(Xstart); self.write_data(0x00); self.write_data(Xend-1)
        self.write_cmd(0x2B); self.write_data(0x00); self.write_data(Ystart); self.write_data(0x00); self.write_data(Yend-1)
        self.write_cmd(0x2C)

    def show(self):
        self.setWindows(0, 0, self.width, self.height)
        self.cs(1); self.dc(1); self.cs(0)
        self.spi.write(self.buffer)
        self.cs(1)

    def init_display(self):
        self.rst(1); time.sleep(0.01); self.rst(0); time.sleep(0.01); self.rst(1); time.sleep(0.05)
        cmds = [
            (0xEF, None), (0xEB, 0x14), (0xFE, None), (0xEF, None), (0xEB, 0x14),
            (0x84, 0x40), (0x85, 0xFF), (0x86, 0xFF), (0x87, 0xFF), (0x88, 0x0A),
            (0x89, 0x21), (0x8A, 0x00), (0x8B, 0x80), (0x8C, 0x01), (0x8D, 0x01),
            (0x8E, 0xFF), (0x8F, 0xFF), (0xB6, bytes([0x00, 0x20])), (0x36, 0x90),
            (0x3A, 0x05), (0x90, bytes([0x08, 0x08, 0x08, 0x08])), (0xBD, 0x06),
            (0xBC, 0x00), (0xFF, bytes([0x60, 0x01, 0x04])), (0xC3, 0x13), (0xC4, 0x13),
            (0xC9, 0x22), (0xBE, 0x11), (0xE1, bytes([0x10, 0x0E])),
            (0xDF, bytes([0x21, 0x0c, 0x02])), (0xF0, bytes([0x45, 0x09, 0x08, 0x08, 0x26, 0x2A])),
            (0xF1, bytes([0x43, 0x70, 0x72, 0x36, 0x37, 0x6F])), (0xF2, bytes([0x45, 0x09, 0x08, 0x08, 0x26, 0x2A])),
            (0xF3, bytes([0x43, 0x70, 0x72, 0x36, 0x37, 0x6F])), (0xED, bytes([0x1B, 0x0B])),
            (0xAE, 0x77), (0xCD, 0x63), (0x70, bytes([0x07, 0x07, 0x04, 0x0E, 0x0F, 0x09, 0x07, 0x08, 0x03])),
            (0xE8, 0x34), (0x62, bytes([0x18, 0x0D, 0x71, 0xED, 0x70, 0x70, 0x18, 0x0F, 0x71, 0xEF, 0x70, 0x70])),
            (0x63, bytes([0x18, 0x11, 0x71, 0xF1, 0x70, 0x70, 0x18, 0x13, 0x71, 0xF3, 0x70, 0x70])),
            (0x64, bytes([0x28, 0x29, 0xF1, 0x01, 0xF1, 0x00, 0x07])),
            (0x66, bytes([0x3C, 0x00, 0xCD, 0x67, 0x45, 0x45, 0x10, 0x00, 0x00, 0x00])),
            (0x67, bytes([0x00, 0x3C, 0x00, 0x00, 0x00, 0x01, 0x54, 0x10, 0x32, 0x98])),
            (0x74, bytes([0x10, 0x85, 0x80, 0x00, 0x00, 0x4E, 0x00])), (0x98, bytes([0x3e, 0x07])),
            (0x35, None), (0x21, None), (0x11, None), (0x29, None)
        ]
        for cmd, data in cmds:
            self.write_cmd(cmd)
            if data:
                if isinstance(data, int): self.write_data(data)
                else:
                    for b in data: self.write_data(b)

# --- GRAPHICS ENGINE (RAM OPTIMIZED) ---
class GraphicsEngine:
    def __init__(self, display, uart):
        self.lcd = display
        self.uart = uart
        gc.collect()
        
        # 1. Empfangspuffer (Klein, 28KB)
        self.incoming_img = bytearray(IMG_SIZE)
        
        # 2. Puffer 2: Der NÄCHSTE Screen (115 KB)
        # Wir brauchen diesen, aber wir sparen uns den dritten "Animation Buffer"
        self.next_screen = bytearray(SCREEN_SIZE)
        
        # 3. Kleiner Mixer-Buffer (Nur für Streifen-Berechnung) - ca 19KB
        # Wir rendern 40 Zeilen auf einmal
        self.CHUNK_LINES = 40 
        self.mixer_buffer = bytearray(SCREEN_W * self.CHUNK_LINES * 2)
        
        # Wrapper für Text-Zeichnen auf next_screen
        self.next_fb = framebuf.FrameBuffer(self.next_screen, SCREEN_W, SCREEN_H, framebuf.RGB565)
        
        self.title = "Waiting..."
        self.artist = "System Ready"
        self.next_title = ""
        self.next_artist = ""
        
        self.state = "IDLE"
        self.anim_progress = 0
        
        # Initial Screen
        self.compose_screen(self.lcd, self.title, self.artist, None)
        self.lcd.show()
        print("RAM-Optimized Engine Ready.")

    def receive_data(self):
        if self.uart.any():
            try:
                line = self.uart.readline()
                if not line: return
                
                if b'IMG_START' in line: 
                    self.load_image_data()
                elif b'TIT:' in line: 
                    self.next_title = line.decode('utf-8').replace('TIT:', '').strip()
                elif b'ART:' in line: 
                    self.next_artist = line.decode('utf-8').replace('ART:', '').strip()
            except: pass

    def load_image_data(self):
        bytes_read = 0
        timeout = time.ticks_ms() + 3000
        
        while bytes_read < IMG_SIZE:
            if time.ticks_ms() > timeout: return
            if self.uart.any():
                chunk = self.uart.read(min(self.uart.any(), IMG_SIZE - bytes_read))
                if chunk:
                    self.incoming_img[bytes_read : bytes_read + len(chunk)] = chunk
                    bytes_read += len(chunk)
                    timeout = time.ticks_ms() + 1000
        
        print("Bild empfangen. Berechne...")
        
        # Wir bereiten den nächsten Screen vor
        self.compose_screen(self.next_fb, self.next_title, self.next_artist, self.incoming_img)
        
        self.state = "ANIMATING"
        self.anim_progress = 0
        
        self.title = self.next_title
        self.artist = self.next_artist

    def draw_text_centered(self, fb, text, y, color):
        text_width = len(text) * 8
        max_width = 180 
        display_text = text
        if text_width > max_width:
            max_chars = (max_width // 8) - 2
            display_text = text[:max_chars] + ".."
            text_width = len(display_text) * 8
        x = (SCREEN_W - text_width) // 2
        fb.text(display_text, x, y, color)

    def compose_screen(self, fb, title, artist, img_data):
        # Framebuffer füllen
        fb.fill(0x0000)
        self.draw_text_centered(fb, title, 35, 0xFFFF)
        self.draw_text_centered(fb, artist, 195, 0x07E0)
        
        # Bild kopieren (Direktzugriff wenn möglich)
        if img_data:
            row_len = 120 * 2
            full_row_len = 240 * 2
            
            # Wir müssen prüfen, ob 'fb' das LCD objekt ist (hat .buffer Attribut) 
            # oder ein FrameBuffer objekt (müssen wir anders behandeln)
            # Einfachster Weg: Wir wissen, dass self.next_fb auf self.next_screen zeigt
            # und self.lcd auf self.lcd.buffer
            
            target_buffer = None
            if hasattr(fb, 'buffer'): target_buffer = fb.buffer
            else: target_buffer = self.next_screen # Fallback für next_fb
            
            for i in range(120):
                src_idx = i * row_len
                dst_idx = ((60 + i) * full_row_len) + (60 * 2)
                target_buffer[dst_idx : dst_idx + row_len] = img_data[src_idx : src_idx + row_len]

    def update(self):
        if self.state == "ANIMATING":
            step = 25 
            self.anim_progress += step
            
            if self.anim_progress >= SCREEN_W:
                self.state = "IDLE"
                # Swap: Inhalt von Next nach Current kopieren
                self.lcd.buffer[:] = self.next_screen[:]
                self.lcd.show()
                gc.collect()
            else:
                # --- CHUNK RENDERER (Das Herzstück der Optimierung) ---
                # Wir berechnen nicht das ganze Bild, sondern nur Streifen.
                
                shift_bytes = self.anim_progress * 2
                row_bytes = SCREEN_W * 2
                rem_bytes = row_bytes - shift_bytes
                
                # Wir iterieren in Schritten von CHUNK_LINES (z.B. 40 Zeilen)
                for y_start in range(0, SCREEN_H, self.CHUNK_LINES):
                    y_end = min(y_start + self.CHUNK_LINES, SCREEN_H)
                    chunk_height = y_end - y_start
                    
                    # Berechnen des Streifens
                    for i in range(chunk_height):
                        global_y = y_start + i
                        line_idx = global_y * row_bytes
                        mix_idx = i * row_bytes # Index im kleinen Mixer Buffer
                        
                        # Links: Alter Screen (aus lcd.buffer)
                        self.mixer_buffer[mix_idx : mix_idx + rem_bytes] = self.lcd.buffer[line_idx + shift_bytes : line_idx + row_bytes]
                        
                        # Rechts: Neuer Screen (aus next_screen)
                        self.mixer_buffer[mix_idx + rem_bytes : mix_idx + row_bytes] = self.next_screen[line_idx : line_idx + shift_bytes]
                    
                    # Streifen sofort senden
                    chunk_size = chunk_height * row_bytes
                    self.lcd.setWindows(0, y_start, 240, y_end)
                    self.lcd.cs(1); self.lcd.dc(1); self.lcd.cs(0)
                    self.lcd.spi.write(self.mixer_buffer[:chunk_size])
                    self.lcd.cs(1)

# --- MAIN ---
if __name__=='__main__':
    uart = UART(0, baudrate=BAUD_RATE, tx=Pin(16), rx=Pin(17))
    lcd = LCD_1inch28()
    engine = GraphicsEngine(lcd, uart)
    
    while True:
        engine.receive_data()
        engine.update()