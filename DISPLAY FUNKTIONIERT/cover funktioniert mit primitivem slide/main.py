from machine import Pin, I2C, SPI, PWM, UART
import framebuf
import time
import gc

# --- CONFIG ---
BAUD_RATE = 921600
IMG_W = 120
IMG_H = 120
IMG_SIZE = IMG_W * IMG_H * 2
BG_COLOR = 0x0000 # Schwarz

# --- DISPLAY TREIBER ---
# (Ich habe die blit_buffer Methode integriert und write_text optimiert)

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

    def init_display(self):
        self.rst(1); time.sleep(0.01); self.rst(0); time.sleep(0.01); self.rst(1); time.sleep(0.05)
        # Gekürzte Init-Sequenz (Standard WaveShare)
        cmds = [
            (0xEF, None), (0xEB, 0x14), (0xFE, None), (0xEF, None), (0xEB, 0x14),
            (0x84, 0x40), (0x85, 0xFF), (0x86, 0xFF), (0x87, 0xFF), (0x88, 0x0A),
            (0x89, 0x21), (0x8A, 0x00), (0x8B, 0x80), (0x8C, 0x01), (0x8D, 0x01),
            (0x8E, 0xFF), (0x8F, 0xFF), (0xB6, bytes([0x00, 0x20])), (0x36, 0x90), # RGB
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
        
    def setWindows(self, Xstart, Ystart, Xend, Yend): 
        self.write_cmd(0x2A); self.write_data(0x00); self.write_data(Xstart); self.write_data(0x00); self.write_data(Xend-1)
        self.write_cmd(0x2B); self.write_data(0x00); self.write_data(Ystart); self.write_data(0x00); self.write_data(Yend-1)
        self.write_cmd(0x2C)
     
    def show(self): 
        self.setWindows(0, 0, self.width, self.height)
        self.cs(1); self.dc(1); self.cs(0)
        self.spi.write(self.buffer)
        self.cs(1)
    
    # Standard Text Methode (schreibt in self.buffer)
    def write_text(self, text, x, y, size, color):
        self.text(text, x, y, color) # Nutzt MicroPython eingebaute Font (8x8)

class GraphicsEngine:
    def __init__(self, display, uart):
        self.lcd = display
        self.uart = uart
        
        # Buffer für die Bilder (Rohdaten)
        self.curr_img = bytearray(IMG_SIZE) 
        self.next_img = bytearray(IMG_SIZE)
        
        # NEU: Ein temporärer Buffer für die Animation
        # Hier bauen wir das "geswipte" Bild Frame für Frame zusammen
        self.anim_buffer = bytearray(IMG_SIZE)
        
        # State
        self.title = "Waiting..."
        self.artist = ""
        self.state = "IDLE" 
        self.anim_progress = 0
        self.image_drawn = False 
        
        # Initial: Alles Schwarz
        self.lcd.fill(BG_COLOR)
        self.lcd.show()
        print("Swipe Engine Ready.")

    def receive_data(self):
        # (Unverändert)
        if self.uart.any():
            try:
                line = self.uart.readline()
                if not line: return
                if b'IMG_START' in line: self.load_image_data()
                elif b'TIT:' in line: self.title = line.decode('utf-8').replace('TIT:', '').strip()
                elif b'ART:' in line: self.artist = line.decode('utf-8').replace('ART:', '').strip()
            except: pass

    def load_image_data(self):
        # (Unverändert)
        bytes_read = 0
        timeout = time.ticks_ms() + 3000 
        while bytes_read < IMG_SIZE:
            if time.ticks_ms() > timeout: return
            if self.uart.any():
                chunk = self.uart.read(min(self.uart.any(), IMG_SIZE - bytes_read))
                if chunk:
                    self.next_img[bytes_read : bytes_read + len(chunk)] = chunk
                    bytes_read += len(chunk)
                    timeout = time.ticks_ms() + 1000 
        
        self.state = "ANIMATING"
        self.anim_progress = 0
        self.image_drawn = False

    def draw_text_centered(self, text, y, color):
        # (Unverändert)
        text_width = len(text) * 8
        max_width = 180 
        display_text = text
        if text_width > max_width:
            max_chars = (max_width // 8) - 2
            display_text = text[:max_chars] + ".."
            text_width = len(display_text) * 8
        x = (240 - text_width) // 2
        self.lcd.write_text(display_text, x, y, 1, color)

    # --- HIER PASSIERT DIE MAGIE ---
    def update(self):
        # 1. Texte vorbereiten (in den Display Buffer)
        self.lcd.fill(0x0000)
        self.draw_text_centered(self.title, 35, 0xFFFF)
        self.draw_text_centered(self.artist, 195, 0x07E0)
        
        # 2. Zonen Oben/Unten senden (Flackerfrei)
        buffer_top = self.lcd.buffer[0 : 28800]
        self.lcd.setWindows(0, 0, 240, 60)
        self.lcd.cs(1); self.lcd.dc(1); self.lcd.cs(0)
        self.lcd.spi.write(buffer_top)
        self.lcd.cs(1)
        
        buffer_bottom = self.lcd.buffer[86400 : ]
        self.lcd.setWindows(0, 180, 240, 240)
        self.lcd.cs(1); self.lcd.dc(1); self.lcd.cs(0)
        self.lcd.spi.write(buffer_bottom)
        self.lcd.cs(1)
        
        # 3. BILD-BEREICH (MITTE)
        
        if self.state == "ANIMATING":
            # Geschwindigkeit des Swipes anpassen (größer = schneller)
            step = 10 
            self.anim_progress += step
            
            if self.anim_progress >= IMG_W:
                # Animation fertig
                self.state = "IDLE"
                self.curr_img[:] = self.next_img[:] # Neues Bild wird zum aktuellen
                # Finales Bild einmal sauber zeichnen
                self.lcd.setWindows(60, 60, 180, 180)
                self.lcd.cs(1); self.lcd.dc(1); self.lcd.cs(0)
                self.lcd.spi.write(self.curr_img)
                self.lcd.cs(1)
                self.image_drawn = True
            else:
                # --- SWIPE BERECHNUNG ---
                # Wir müssen das anim_buffer Frame für Frame zusammenbauen.
                
                # Breite einer Zeile in Bytes (120 Pixel * 2 Bytes)
                row_bytes = IMG_W * 2
                # Wieviele Bytes haben wir uns schon verschoben?
                shift_bytes = self.anim_progress * 2
                # Wieviele Bytes vom alten Bild sind noch übrig?
                rem_bytes = row_bytes - shift_bytes
                
                # Wir iterieren durch alle 120 Zeilen des Bildes
                for i in range(IMG_H):
                    # Start-Index der aktuellen Zeile im Buffer
                    idx = i * row_bytes
                    
                    # TEIL 1: Das ALTE Bild (rutscht nach links raus)
                    # Wir nehmen den rechten Teil des alten Bildes...
                    old_slice = self.curr_img[idx + shift_bytes : idx + row_bytes]
                    # ...und kopieren ihn an den Anfang der Zeile im Animations-Buffer
                    self.anim_buffer[idx : idx + rem_bytes] = old_slice
                    
                    # TEIL 2: Das NEUE Bild (rutscht von rechts rein)
                    # Wir nehmen den linken Teil des neuen Bildes...
                    new_slice = self.next_img[idx : idx + shift_bytes]
                    # ...und kopieren ihn an das Ende der Zeile im Animations-Buffer
                    self.anim_buffer[idx + rem_bytes : idx + row_bytes] = new_slice
                
                # --- ENDE BERECHNUNG ---
                
                # Das fertig zusammengebaute Zwischenbild senden
                self.lcd.setWindows(60, 60, 180, 180)
                self.lcd.cs(1); self.lcd.dc(1); self.lcd.cs(0)
                self.lcd.spi.write(self.anim_buffer) 
                self.lcd.cs(1)
                
        elif self.state == "IDLE":
            if not self.image_drawn:
                self.lcd.setWindows(60, 60, 180, 180)
                self.lcd.cs(1); self.lcd.dc(1); self.lcd.cs(0)
                self.lcd.spi.write(self.curr_img)
                self.lcd.cs(1)
                self.image_drawn = True
# --- MAIN ---
if __name__=='__main__':
    # Initialisierung Hardware
    # WICHTIG: Baudrate muss exakt stimmen
    uart = UART(0, baudrate=BAUD_RATE, tx=Pin(16), rx=Pin(17))
    
    lcd = LCD_1inch28() 
    engine = GraphicsEngine(lcd, uart)
    
    print("Engine Running...")
    
    while True:
        engine.receive_data()
        engine.update()
