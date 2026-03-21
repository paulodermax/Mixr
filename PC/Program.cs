using System;
using System.Drawing;
using System.IO.Ports;
using System.Text;
using System.Threading;

class Program
{
    const byte PKT_START_BYTE = 0xAA;
    
    // Paket-Typen (synchron mit protocol.h auf dem ESP32)
    const byte TYPE_SONG_TITLE  = 0x01;
    const byte TYPE_SONG_ARTIST = 0x02;
    const byte TYPE_SLIDER_VALS = 0x03;
    const byte TYPE_BTN_CMD     = 0x04;
    const byte TYPE_IMAGE_CHUNK = 0x05; // Neu für die Bild-Chunks

    static void Main()
    {
        string portName = "COM6";

        try
        {
            using (SerialPort port = new SerialPort(portName, 115200))
            {
                port.Open();
                Console.WriteLine($"Verbunden mit {portName}.");

                // 1. Hintergrund-Thread zum Empfangen der ESP32-Daten starten
                Thread readThread = new Thread(ReadLoop);
                readThread.IsBackground = true;
                readThread.Start(port);

                // 2. Metadaten senden
                SendPacket(port, TYPE_SONG_TITLE, Encoding.UTF8.GetBytes("Cyberpunk 2077 Theme"));
                SendPacket(port, TYPE_SONG_ARTIST, Encoding.UTF8.GetBytes("Marcin Przybylowicz"));
                Console.WriteLine("Song-Daten gesendet.");

                // 3. Bild in 200-Byte Chunks senden
                SendImageInChunks(port, "cover.jpg");
                
                Console.WriteLine("Warte auf Hardware-Eingaben vom ESP32...");
                Console.WriteLine("Drücke ENTER zum Beenden.");
                Console.ReadLine();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Fehler: {ex.Message}");
        }
    }

    // --- EMPFANGEN (ESP32 -> PC) ---
    static void ReadLoop(object? obj)
    {
        if (obj is not SerialPort port) return;
        while (port.IsOpen)
        {
            try
            {
                if (port.BytesToRead > 0 && port.ReadByte() == PKT_START_BYTE)
                {
                    int len = port.ReadByte();
                    int type = port.ReadByte();
                    
                    byte[] payload = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        read += port.Read(payload, read, len - read);
                    }
                    
                    int crc = port.ReadByte();
                    int calcCrc = (len ^ type);
                    foreach (byte b in payload) calcCrc ^= b;

                    if (crc == calcCrc)
                    {
                        ProcessIncomingData(type, payload);
                    }
                }
            }
            catch (TimeoutException) { }
            catch (Exception) { break; }
        }
    }

    static void ProcessIncomingData(int type, byte[] payload)
    {
        if (type == TYPE_SLIDER_VALS && payload.Length >= 4)
        {
            Console.WriteLine($"[ESP32] Slider: S1={payload[0]}, S2={payload[1]}, S3={payload[2]}, S4={payload[3]} (4 Kanäle)");
        }
        else if (type == TYPE_BTN_CMD && payload.Length >= 1)
        {
            Console.WriteLine($"[ESP32] Button Command: {payload[0]}");
        }
    }

    // --- SENDEN (PC -> ESP32) ---
    static void SendPacket(SerialPort port, byte type, byte[] payload)
    {
        if (payload.Length > 255) return;

        byte length = (byte)payload.Length;
        byte crc = (byte)(length ^ type);
        foreach (byte b in payload) crc ^= b;

        byte[] packet = new byte[3 + payload.Length + 1];
        packet[0] = PKT_START_BYTE;
        packet[1] = length;
        packet[2] = type;
        Buffer.BlockCopy(payload, 0, packet, 3, payload.Length);
        packet[packet.Length - 1] = crc;

        port.Write(packet, 0, packet.Length);
        
        // Reguläre Pause (bei ImageChunks wird diese durch Thread.Sleep(2) überlagert)
        if (type != TYPE_IMAGE_CHUNK) Thread.Sleep(50); 
    }

    // --- BILD-VERARBEITUNG ---
    static void SendImageInChunks(SerialPort port, string imagePath)
    {
        byte[] rgb565Data = ConvertImageToRgb565(imagePath);
        int chunkSize = 250; 
        int totalChunks = (int)Math.Ceiling((double)rgb565Data.Length / chunkSize);

        Console.WriteLine($"Sende Bild ({rgb565Data.Length} Bytes) in {totalChunks} Paketen...");

        for (int i = 0; i < rgb565Data.Length; i += chunkSize)
        {
            int currentChunkSize = Math.Min(chunkSize, rgb565Data.Length - i);
            byte[] chunk = new byte[currentChunkSize];
            Buffer.BlockCopy(rgb565Data, i, chunk, 0, currentChunkSize);

            SendPacket(port, TYPE_IMAGE_CHUNK, chunk);
            // Keine Verzögerung mehr. Hardware übernimmt Flow-Control.
        }
        Console.WriteLine("Bildübertragung abgeschlossen.");
    }

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
                    ushort rgb565 = (ushort)(((c.R & 0xF8) << 8) | ((c.G & 0xFC) << 3) | (c.B >> 3));
                    
                    // Korrigierte Endianness
                    buffer[i++] = (byte)((rgb565 >> 8) & 0xFF);  
                    buffer[i++] = (byte)(rgb565 & 0xFF);         
                }
            }
            return buffer;
        }
    }
    static void OnSongChanged(SerialPort port, string title, string artist, string imagePath)
    {
        // 1. Texte senden (Priorität hoch)
        SendPacket(port, TYPE_SONG_TITLE, Encoding.UTF8.GetBytes(title));
        SendPacket(port, TYPE_SONG_ARTIST, Encoding.UTF8.GetBytes(artist));
        
        // 2. Bild senden (Dauert ca. 150ms)
        SendImageInChunks(port, imagePath);
    }
}