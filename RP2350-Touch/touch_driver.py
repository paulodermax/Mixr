from machine import Pin
import time

# Gesten Mapping
GESTURES = {
    0x00: "NONE",
    0x01: "SWIPE_UP",
    0x02: "SWIPE_DOWN",
    0x03: "SWIPE_LEFT",
    0x04: "SWIPE_RIGHT",
    0x05: "CLICK",
    0x0B: "DOUBLE_CLICK",
    0x0C: "LONG_PRESS"
}

class Touch_CST816S:
    def __init__(self, i2c, address=0x15, rst_pin=22, int_pin=21):
        self._bus = i2c
        self._address = address
        self._rst = Pin(rst_pin, Pin.OUT)
        self._int = Pin(int_pin, Pin.IN, Pin.PULL_UP)
        self.reset()
        
    def reset(self):
        self._rst(0)
        time.sleep(0.01)
        self._rst(1)
        time.sleep(0.05)

    def get_touch(self):
        """Liest Touch-Koordinaten und Gesten. Gibt (Geste, X, Y) oder None zurück."""
        # Das Interrupt-Pin (INT) geht auf LOW, wenn Daten bereitstehen
        if self._int.value() == 1:
            return None
            
        try:
            # Lese 6 Bytes ab Register 0x01
            data = self._bus.readfrom_mem(self._address, 0x01, 6)
            
            gesture_id = data[0]
            points = data[1]
            x_point = ((data[2] & 0x0F) << 8) + data[3]
            y_point = ((data[4] & 0x0F) << 8) + data[5]
            
            gesture_name = GESTURES.get(gesture_id, "NONE")
            
            if points == 0 and gesture_id == 0:
                return None
                
            return (gesture_name, x_point, y_point)
        except OSError:
            return None
