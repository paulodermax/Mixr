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
            LoggerService.Info($"📤 Starte Highspeed Bild-Upload ({imageBytes.Length} Bytes)...");

            // 1. Start-Tag senden
            _serialPort!.Write("<IMG>");

            // 2. Volle Ladung: Das gesamte Array in einem einzigen Rutsch senden!
            // Kein Chunking, keine kuenstlichen Pausen mehr.
            _serialPort.Write(imageBytes, 0, imageBytes.Length);

            // 3. Ende-Tag senden (Der Pi ignoriert es zwar durch den Byte-Zähler,
            // aber es hält den Datenstrom sauber, falls sich mal was verschiebt).
            _serialPort.Write("<END>");
            
            // Alles sofort aus dem Windows-Puffer drücken
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