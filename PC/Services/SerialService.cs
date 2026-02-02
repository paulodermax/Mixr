using System;
using System.IO.Ports;
using System.Threading;

namespace Mixr.Services;

public class SerialService
{
    private SerialPort? _serialPort;
    public event Action<string>? OnDataReceived;
    
    public bool IsOpen => _serialPort != null && _serialPort.IsOpen;

    public bool Connect(string portName, int baudRate)
    {
        Close();

        try
        {
            _serialPort = new SerialPort(portName, baudRate);
            _serialPort.NewLine = "\n";
            
            // WICHTIG: Flusssteuerung aktivieren, damit der Puffer nicht überläuft
            _serialPort.DtrEnable = true; 
            _serialPort.RtsEnable = true;
            
            // Puffergrößen erhöhen
            _serialPort.WriteBufferSize = 8192;
            _serialPort.ReadBufferSize = 8192;

            _serialPort.DataReceived += (s, e) => 
            {
                try 
                {
                    if (_serialPort.IsOpen)
                    {
                        string line = _serialPort.ReadLine().Trim();

                        OnDataReceived?.Invoke(line);
                    }
                } 
                catch { }
            };
            
            _serialPort.Open();
            _serialPort.DiscardInBuffer();
            // Kurze Wartezeit nach dem Öffnen
            Thread.Sleep(500);
            return true;
        }
        catch (Exception ex)
        {
            LoggerService.Error($"Fehler beim Öffnen von {portName}", ex);
            return false;
        }
    }

    public void Send(string data)
    {
        if (IsOpen)
        {
            try 
            { 
                _serialPort!.Write(data);
                // WICHTIG: Sofort rausschicken!
                _serialPort.BaseStream.Flush(); 
            } 
            catch { }
        }
    }

    public void SendImage(byte[] imageBytes)
    {
        if (!IsOpen || imageBytes.Length == 0) return;

        try
        {
            LoggerService.Info("📤 Starte Bild-Upload...");

            // 1. Start-Tag senden & flushen
            _serialPort!.Write("<IMG>");
            _serialPort.BaseStream.Flush();
            Thread.Sleep(50); // Kurz warten, damit Python umschalten kann

            // 2. Daten in kleinen Chunks senden (Paketweise)
            // Wir nehmen kleine Pakete (64 Byte), das ist langsamer aber VIEL sicherer
            int chunkSize = 64; 
            for (int i = 0; i < imageBytes.Length; i += chunkSize)
            {
                int remaining = Math.Min(chunkSize, imageBytes.Length - i);
                
                _serialPort.Write(imageBytes, i, remaining);
                _serialPort.BaseStream.Flush(); // Zwingt Windows, die Daten SOFORT zu senden
                
                // Eine winzige Pause gibt dem Raspberry Pi Zeit, den Puffer zu leeren
                // Ohne das verschluckt sich der Linux-Gadget-Treiber oft
                Thread.Sleep(2); 
            }

            // 3. Ende-Tag senden & flushen
            Thread.Sleep(50); // Sicherheitsabstand zum letzten Datenbyte
            _serialPort.Write("<END>");
            _serialPort.BaseStream.Flush();
            
            LoggerService.Info("✅ Bild-Upload abgeschlossen.");
        }
        catch (Exception ex)
        {
            LoggerService.Error("Fehler beim Senden des Bildes", ex);
        }
    }

    public void Close()
    {
        try 
        { 
            if (_serialPort != null && _serialPort.IsOpen)
            {
                // DTR zurücksetzen signalisiert dem Pi oft "Verbindung getrennt"
                _serialPort.DtrEnable = false;
                _serialPort.RtsEnable = false;
                _serialPort.Close();
                _serialPort.Dispose();
            }
        } 
        catch { }
        _serialPort = null;
    }
}