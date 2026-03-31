using System;
using System.Collections.Generic;
using System.IO;

namespace Mixr_App;

/// <summary>
/// Schreibt nach mixr_app.log — zuerst neben der EXE, bei Fehler unter %LocalAppData%\Mixr, sonst %TEMP%.
/// </summary>
static class AppLog
{
    static readonly object LockObj = new();
    static string? _resolvedPath;

    static string ExeDirectory()
    {
        try
        {
            var p = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(p))
            {
                var d = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(d))
                    return d;
            }
        }
        catch
        {
            /* */
        }

        var b = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(b))
            return b.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Environment.CurrentDirectory;
    }

    static IEnumerable<string> CandidatePaths()
    {
        var exeDir = ExeDirectory();
        if (!string.IsNullOrEmpty(exeDir))
            yield return Path.Combine(exeDir, "mixr_app.log");

        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Mixr",
            "mixr_app.log");
        yield return local;

        yield return Path.Combine(Path.GetTempPath(), "mixr_app.log");
    }

    /// <summary>Aktive Datei nach erstem erfolgreichen Schreiben, sonst bevorzugter Pfad.</summary>
    public static string LogFilePath => _resolvedPath ?? Path.Combine(ExeDirectory(), "mixr_app.log");

    public static void WriteLine(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
        lock (LockObj)
        {
            foreach (var path in CandidatePaths())
            {
                try
                {
                    var dir = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
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

    public static void WriteException(Exception ex)
    {
        WriteLine(ex.ToString());
    }
}
