import spidev
import time
import serial

ser=serial.Serial('/dev/ttyGS0',230400)

NUM_SLIDERS = 5
THRESHOLD = 10
READ_INTERVAL = 0.1

spi = spidev.SpiDev()
spi.open(0,0)
spi.max_speed_hz = 1350000

prev_values = [-1]*NUM_SLIDERS

def read_channel(channel):
		r = spi.xfer2([1, (8 + channel) << 4, 0])
		result = ((r[1] & 3) << 8) | r[2]
		return result

while True:
	changed = False
	current_values =[]
	for ch in range(NUM_SLIDERS):
		val = read_channel(ch)
		current_values.append(val)
		if prev_values[ch] == -1 or abs(val - prev_values[ch]) > THRESHOLD:
			changed=True

	if changed:
		print("|".join(str(v) for v in current_values))
		line ="|".join(str(v) for v in current_values)
		prev_values=current_values.copy()
		ser.write((line+"\r\n").encode("utf-8"))

	time.sleep(READ_INTERVAL)
