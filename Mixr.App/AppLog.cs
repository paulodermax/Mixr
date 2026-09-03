using System;
using System.IO;
using Mixr.Services;

namespace Mixr_App;

/// <summary>
/// Schreibt nach <c>%LOCALAPPDATA%\Mixr\logs\mixr_app.log</c> (Fallback: %TEMP%). Rotation bei 2 MB, 3 Generationen.
/// Der Programmordner wird nie beschrieben — Velopack ersetzt ihn bei jedem Update.
/// </summary>
static class AppLog
{
    const long MaxBytes = 2 * 1024 * 1024;
    const int Generations = 3;
    const string FileName = "mixr_app.log";

    static readonly object LockObj = new();
    static string? _resolvedPath;

    static string PreferredPath()
    {
        try
        {
            return Path.Combine(MixrConfigPaths.LogDir, FileName);
        }
        catch
        {
            return Path.Combine(Path.GetTempPath(), FileName);
        }
    }

    public static string LogFilePath => _resolvedPath ?? PreferredPath();

    public static string LogDirectory => Path.GetDirectoryName(LogFilePath) ?? Path.GetTempPath();

    public static void WriteLine(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (LockObj)
        {
            foreach (var path in new[] { PreferredPath(), Path.Combine(Path.GetTempPath(), FileName) })
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    RotateIfNeeded(path);
                    File.AppendAllText(path, line);
                    _resolvedPath = path;
                    return;
                }
                catch
                {
                    /* nächster Kandidat */
                }
            }
        }
    }

    public static void WriteException(Exception ex) => WriteLine(ex.ToString());

    static void RotateIfNeeded(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || fi.Length < MaxBytes)
            return;

        for (var i = Generations - 1; i >= 1; i--)
        {
            var from = $"{path}.{i}";
            var to = $"{path}.{i + 1}";
            if (File.Exists(from))
                File.Move(from, to, overwrite: true);
        }

        File.Move(path, $"{path}.1", overwrite: true);
    }
}
