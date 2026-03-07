using System;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace Mixr.Services;

public class SerialService : IDisposable
{
    private SerialPort? _serialPort;
    private Thread? _readThread;
    private bool _isRunning;

    // Protokoll-Konstanten
    const byte PKT_START_BYTE = 0xAA;
    const byte TYPE_SONG_TITLE  = 0x01;
    const byte TYPE_SONG_ARTIST = 0x02;
    const byte TYPE_SLIDER_VALS = 0x03;
    const byte TYPE_IMAGE_CHUNK = 0x05;

    public event Action<byte[]>? OnSliderDataReceived;
    
    public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

    public bool Connect(string portName, int baudRate)
    {
        Close();

        try
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.DtrEnable = true; 
            _serialPort.RtsEnable = true;
            _serialPort.WriteBufferSize = 8192;
            _serialPort.ReadBufferSize = 8192;
            
            _serialPort.Open();
            _serialPort.DiscardInBuffer();

            _isRunning = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            Thread.Sleep(500);
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.Error($"Fehler beim Öffnen von {portName}", ex);
            return false;
        }
    }

    private void ReadLoop()
    {
        while (_isRunning && _serialPort != null && _serialPort.IsOpen)
        {
            try
            {
                if (_serialPort.BytesToRead > 0 && _serialPort.ReadByte() == PKT_START_BYTE)
                {
                    int len = _serialPort.ReadByte();
                    int type = _serialPort.ReadByte();
                    
                    byte[] payload = new byte[len];
                    int read = 0;
                    while (read < len)
                    {
                        read += _serialPort.Read(payload, read, len - read);
                    }
                    
                    int crc = _serialPort.ReadByte();
                    int calcCrc = (len ^ type);
                    foreach (byte b in payload) calcCrc ^= b;

                    // Wenn Slider-Werte reinkommen, ans System weiterleiten
                    if (crc == calcCrc && type == TYPE_SLIDER_VALS)
                    {
                        OnSliderDataReceived?.Invoke(payload);
                    }
                }
                else
                {
                    Thread.Sleep(1); // CPU entlasten
                }
            }
            catch { /* Timeout ignorieren */ }
        }
    }

    public void SendSongData(string title, string artist)
    {
        SendPacket(TYPE_SONG_TITLE, Encoding.UTF8.GetBytes(title ?? ""));
        SendPacket(TYPE_SONG_ARTIST, Encoding.UTF8.GetBytes(artist ?? ""));
    }

    public void SendImageInChunks(byte[] imageData)
    {
        if (!IsOpen || imageData.Length == 0) return;

        try
        {
            LoggerService.Info($"📤 Starte Highspeed Bild-Upload ({imageData.Length} Bytes)...");
            int chunkSize = 250; 
            
            for (int i = 0; i < imageData.Length; i += chunkSize)
            {
                int currentChunkSize = Math.Min(chunkSize, imageData.Length - i);
                byte[] chunk = new byte[currentChunkSize];
                Buffer.BlockCopy(imageData, i, chunk, 0, currentChunkSize);
                
                SendPacket(TYPE_IMAGE_CHUNK, chunk);
            }
            LoggerService.Info("✅ Bild-Upload abgeschlossen.");
        }
        catch (Exception ex)
        {
            LoggerService.Error("Fehler beim Senden des Bildes", ex);
        }
    }

    private void SendPacket(byte type, byte[] payload)
    {
        if (payload.Length > 255 || !IsOpen) return;

        byte length = (byte)payload.Length;
        byte crc = (byte)(length ^ type);
        foreach (byte b in payload) crc ^= b;

        byte[] packet = new byte[3 + payload.Length + 1];
        packet[0] = PKT_START_BYTE;
        packet[1] = length;
        packet[2] = type;
        Buffer.BlockCopy(payload, 0, packet, 3, payload.Length);
        packet[packet.Length - 1] = crc;

        _serialPort!.Write(packet, 0, packet.Length);
    }

    public void Close()
    {
        _isRunning = false;
        try 
        { 
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.DtrEnable = false;
                _serialPort.RtsEnable = false;
                _serialPort.Close();
                _serialPort.Dispose();
            }
        } 
        catch { }
        _serialPort = null;
    }

    public void Dispose() => Close();
}