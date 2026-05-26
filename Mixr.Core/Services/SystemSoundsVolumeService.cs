using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;

namespace Mixr.Services;

/// <summary>Setzt die Windows-„System Sounds“-Session im Mixer (Benachrichtigungen o. Ä.).</summary>
public static class SystemSoundsVolumeService
{
    const double DefaultCapPercent = 20.0;

    static readonly string[] SystemSoundsLabels =
    [
        "System Sounds",
        "Systemlaute",
        "Sons système",
    ];

    public static bool TryApplyCap(double maxPercent = DefaultCapPercent)
    {
        try
        {
            var controller = new CoreAudioController();
            var device = controller.DefaultPlaybackDevice as CoreAudioDevice;
            if (device?.SessionController == null)
                return false;

            var sessions = device.SessionController.ActiveSessionsAsync().GetAwaiter().GetResult();
            foreach (var session in sessions)
            {
                if (!IsSystemSoundsSession(session))
                    continue;

                if (session.Volume > maxPercent)
                    session.Volume = maxPercent;
                return true;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mixr System Sounds: {ex.Message}");
        }

        return false;
    }

    static bool IsSystemSoundsSession(IAudioSession session)
    {
        var name = session.DisplayName ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return false;

        foreach (var label in SystemSoundsLabels)
        {
            if (name.Equals(label, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return name.Contains("system", StringComparison.OrdinalIgnoreCase) &&
               name.Contains("sound", StringComparison.OrdinalIgnoreCase);
    }
}
