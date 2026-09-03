using System.Text;

namespace Mixr.Services;

/// <summary>Datei erst als <c>.tmp</c> schreiben, dann atomar über das Ziel schieben.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        try
        {
            File.WriteAllText(tmp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                try
                {
                    File.Delete(tmp);
                }
                catch
                {
                    /* Aufräumen ist optional */
                }
            }
        }
    }
}
