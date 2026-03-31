using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using System.Diagnostics;

namespace Mixr.Services;

/// <summary>Master-Lautstärke + App-Sessions (AudioSwitcher 3.0.3).</summary>
public sealed class AudioService
{
    private readonly CoreAudioController _controller = new();
    private Dictionary<string, List<IAudioSession>> _sessionMap = new(StringComparer.OrdinalIgnoreCase);

    public void RebuildSessionMap(
        IReadOnlyList<string> mappings,
        IReadOnlyDictionary<string, List<string>> groups,
        bool silent = false)
    {
        Task.Run(async () => await RebuildSessionMapAsync(mappings, groups, silent)).Wait();
    }

    private async Task RebuildSessionMapAsync(
        IReadOnlyList<string> mappings,
        IReadOnlyDictionary<string, List<string>> groups,
        bool silent)
    {
        _ = silent;
        _sessionMap.Clear();

        var device = _controller.DefaultPlaybackDevice as CoreAudioDevice;
        if (device?.SessionController == null)
            return;

        var sessionsEnum = await device.SessionController.ActiveSessionsAsync();
        var sessions = sessionsEnum.ToList();

        foreach (var session in sessions)
        {
            string name = session.DisplayName;

            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    name = Process.GetProcessById(session.ProcessId).ProcessName;
                }
                catch
                {
                    continue;
                }
            }

            if (string.IsNullOrEmpty(name))
                continue;

            string? matched = mappings.FirstOrDefault(m => name.Equals(m, StringComparison.OrdinalIgnoreCase))
                ?? mappings.FirstOrDefault(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

            if (matched == null && groups.Count > 0)
            {
                var eq = groups.FirstOrDefault(g =>
                    g.Value.Any(k => name.Equals(k, StringComparison.OrdinalIgnoreCase)));
                if (eq.Key != null)
                    matched = eq.Key;
                else
                {
                    var sub = groups.FirstOrDefault(g =>
                        g.Value.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase)));
                    matched = sub.Key;
                }
            }

            if (matched == null)
                continue;

            if (!_sessionMap.ContainsKey(matched))
                _sessionMap[matched] = new List<IAudioSession>();

            _sessionMap[matched].Add(session);
        }
    }

    public void SetVolume(string target, float level)
    {
        Task.Run(() => ApplyVolume(target, level));
    }

    private void ApplyVolume(string target, float level)
    {
        try
        {
            var pct = level * 100.0;
            var device = _controller.DefaultPlaybackDevice as CoreAudioDevice;
            if (device == null)
                return;

            if (target.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                device.Volume = pct;
                return;
            }

            if (_sessionMap.TryGetValue(target, out var list))
            {
                foreach (var session in list)
                {
                    try
                    {
                        session.Volume = pct;
                    }
                    catch
                    {
                        /* Session ungültig */
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Mixr Audio: {target}: {ex.Message}");
        }
    }
}
