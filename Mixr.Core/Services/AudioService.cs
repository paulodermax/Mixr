using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using System.Diagnostics;

namespace Mixr.Services;

/// <summary>Master-Lautstärke + App-Sessions (AudioSwitcher 3.0.3).</summary>
public sealed class AudioService
{
    private readonly CoreAudioController _controller = new();
    private Dictionary<string, List<IAudioSession>> _sessionMap = new(StringComparer.OrdinalIgnoreCase);

    readonly object _snapshotLock = new();
    Dictionary<string, List<string>> _liveNamesByGroup = new(StringComparer.OrdinalIgnoreCase);

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
        {
            lock (_snapshotLock)
                _liveNamesByGroup = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            return;
        }

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

        BuildLiveSnapshot();
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
