using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using System.Diagnostics;

namespace Mixr.Services;

/// <summary>Master-Lautstärke + App-Sessions (AudioSwitcher 3.0.3).</summary>
public sealed class AudioService : IDisposable
{
    private readonly CoreAudioController _controller = new();
    bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        try
        {
            _controller.Dispose();
        }
        catch
        {
            /* COM bereits abgebaut */
        }
    }
    private Dictionary<string, List<IAudioSession>> _sessionMap = new(StringComparer.OrdinalIgnoreCase);

    readonly object _snapshotLock = new();
    readonly object _rebuildLock = new();
    Dictionary<string, List<string>> _liveNamesByGroup = new(StringComparer.OrdinalIgnoreCase);

    public void RebuildSessionMap(
        IReadOnlyList<string> mappings,
        IReadOnlyDictionary<string, List<string>> groups,
        bool silent = false)
    {
        lock (_rebuildLock)
            Task.Run(async () => await RebuildSessionMapAsync(mappings, groups, silent)).Wait();
    }

    private async Task RebuildSessionMapAsync(
        IReadOnlyList<string> mappings,
        IReadOnlyDictionary<string, List<string>> groups,
        bool silent)
    {
        _ = silent;
        var nextMap = new Dictionary<string, List<IAudioSession>>(StringComparer.OrdinalIgnoreCase);

        var device = _controller.DefaultPlaybackDevice as CoreAudioDevice;
        if (device?.SessionController == null)
        {
            lock (_snapshotLock)
                _liveNamesByGroup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var sessionsEnum = await device.SessionController.ActiveSessionsAsync();
        var sessions = sessionsEnum.ToList();

        foreach (var session in sessions)
        {
            var names = SessionNamesFor(session);
            if (names.Count == 0)
                continue;

            string? matched = null;
            foreach (var name in names)
            {
                matched = SessionTokenMatcher.MatchToMapping(name, mappings)
                    ?? (groups.Count > 0 ? SessionTokenMatcher.MatchToGroupKey(name, groups) : null);
                if (matched != null)
                    break;
            }

            if (matched == null)
                continue;

            if (!nextMap.ContainsKey(matched))
                nextMap[matched] = new List<IAudioSession>();

            nextMap[matched].Add(session);
        }

        _sessionMap = nextMap;
        BuildLiveSnapshot();
    }

    static List<string> SessionNamesFor(IAudioSession session)
    {
        var names = new List<string>(2);
        if (!string.IsNullOrWhiteSpace(session.DisplayName))
            names.Add(session.DisplayName.Trim());

        try
        {
            var proc = Process.GetProcessById(session.ProcessId).ProcessName;
            if (!string.IsNullOrWhiteSpace(proc) &&
                !names.Any(n => n.Equals(proc, StringComparison.OrdinalIgnoreCase)))
                names.Add(proc.Trim());
        }
        catch
        {
            /* Prozess beendet */
        }

        return names;
    }

    void BuildLiveSnapshot()
    {
        var snap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in _sessionMap)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var session in kv.Value)
            {
                var label = session.DisplayName;
                if (string.IsNullOrEmpty(label))
                {
                    try
                    {
                        label = Process.GetProcessById(session.ProcessId).ProcessName;
                    }
                    catch
                    {
                        continue;
                    }
                }

                if (!string.IsNullOrEmpty(label))
                    set.Add(label);
            }

            if (set.Count > 0)
                snap[kv.Key] = set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        lock (_snapshotLock)
            _liveNamesByGroup = snap;
    }

    /// <summary>Lautstärke 0–1 pro Slider-Key nach letztem Session-Scan (master = Gerät, sonst Sessions).</summary>
    public float[] GetVolumeLevels(IReadOnlyList<string> mappings)
    {
        var levels = new float[mappings.Count];
        for (var i = 0; i < levels.Length; i++)
            levels[i] = -1;

        try
        {
            var device = _controller.DefaultPlaybackDevice as CoreAudioDevice;
            if (device == null)
                return levels;

            for (var i = 0; i < mappings.Count; i++)
            {
                var key = mappings[i];
                if (key.Equals("master", StringComparison.OrdinalIgnoreCase))
                {
                    levels[i] = (float)Math.Clamp(device.Volume / 100.0, 0, 1);
                    continue;
                }

                if (!_sessionMap.TryGetValue(key, out var sessions) || sessions.Count == 0)
                    continue;

                double sum = 0;
                var count = 0;
                foreach (var session in sessions)
                {
                    try
                    {
                        sum += session.Volume;
                        count++;
                    }
                    catch
                    {
                        /* Session ungültig */
                    }
                }

                if (count > 0)
                    levels[i] = (float)Math.Clamp(sum / count / 100.0, 0, 1);
            }
        }
        catch
        {
            /* Gerät/Sessions nicht lesbar */
        }

        return levels;
    }

    /// <summary>Aktive Audio-Sessions pro Gruppen-Key (master, communication, …) nach letztem Scan.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> GetLiveSnapshot()
    {
        lock (_snapshotLock)
        {
            return _liveNamesByGroup.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<string>)kv.Value.ToList(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SetVolume(string target, float level)
    {
        ApplyVolume(target, level);
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
