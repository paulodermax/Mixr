using Serilog;
using System;
using System.IO;

namespace Mixr.Services;

public static class LoggerService 
{
    public static void Initialize() 
    {
        // 1. Pfad zum "logs" Ordner definieren
        string logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
        
        // 2. Den vollen Pfad zur Datei bauen (z.B. .../bin/Debug/.../logs/log.txt)
        string logPath = Path.Combine(logDirectory, "log.txt");

        Log.Logger = new LoggerConfiguration()
            .WriteTo.Console() // Konsole bleibt wie sie ist
            .WriteTo.File(
                path: logPath, 
                rollingInterval: RollingInterval.Day, // Macht daraus log20260128.txt
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}" // Datum im Text sparen
            )
            .CreateLogger();
    }

    public static void Info(string message) => Log.Information(message);
    
    public static void Warn(string message) => Log.Warning(message);
    
    public static void Error(string message, Exception? ex = null) 
    {
        if (ex != null)
            Log.Error(ex, message);
        else
            Log.Error(message);
    }
}