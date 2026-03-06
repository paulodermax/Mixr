using System;
using System.Drawing;
using System.IO.Ports;

class Program
{
    static void Main()
    {
        // COM-Port deines ESP32-S3 eintragen (prüfe den Geräte-Manager)
        string portName = "COM6"; 
        string imagePath = "cover.jpg"; // Pfad zu einem beliebigen Testbild

        try
        {
            byte[] rgb565Data = ConvertImageToRgb565(imagePath);

            using (SerialPort port = new SerialPort(portName, 115200))
            {
                port.Open();
                Console.WriteLine($"Verbunden mit {portName}. Sende Daten...");

                // 1. Sende das Start-Byte (0x02)
                port.Write(new byte[] { 0x02 }, 0, 1);
                
                // 2. Sende die exakt 115.200 Bytes des Bildes
                port.Write(rgb565Data, 0, rgb565Data.Length);
                
                Console.WriteLine("Cover in < 150 ms übertragen.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler: {ex.Message}");
        }
        
        Console.ReadLine();
    }

    // Skaliert das Bild auf 240x240 und konvertiert in RGB565-Byte-Array
    static byte[] ConvertImageToRgb565(string path)
    {
        using (Bitmap img = new Bitmap(Image.FromFile(path), new Size(240, 240)))
        {
            byte[] buffer = new byte[240 * 240 * 2];
            int i = 0;

            for (int y = 0; y < 240; y++)
            {
                for (int x = 0; x < 240; x++)
                {
                    Color c = img.GetPixel(x, y);
                    
                    // RGB565 Bit-Shifting
                    ushort rgb565 = (ushort)(((c.R & 0xF8) << 8) | ((c.G & 0xFC) << 3) | (c.B >> 3));
                    
                    // Little-Endian (ESP32 / LVGL Standard)
                    buffer[i++] = (byte)(rgb565 & 0xFF);
                    buffer[i++] = (byte)((rgb565 >> 8) & 0xFF);
                }
            }
            return buffer;
        }
    }
}