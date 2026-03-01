using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Mixr.Services;

public class AudioService
{
    private readonly CoreAudioController _controller = new CoreAudioController();
    private Dictionary<string, List<IAudioSession>> _sessionMap = new();

    // Parameter 'silent' steuert Logging
    public void RebuildSessionMap(List<string> mappings, Dictionary<string, List<string>> groups, bool silent = false)
    {
        Task.Run(async () => await RebuildSessionMapAsync(mappings, groups, silent)).Wait();
    }

    private async Task RebuildSessionMapAsync(List<string> mappings, Dictionary<string, List<string>> groups, bool silent)
    {
        _sessionMap.Clear();

        var device = _controller.DefaultPlaybackDevice;
        if (device == null) return;

        var sessionController = device.GetCapability<IAudioSessionController>();
        if (sessionController == null) return;

        var sessions = await sessionController.ActiveSessionsAsync();

        foreach (var session in sessions)
        {
            string name = session.DisplayName;

            if (string.IsNullOrEmpty(name))
            {
                try
                {
                    var p = Process.GetProcessById(session.ProcessId);
                    name = p.ProcessName; 
                }
                catch 
                { 
                    continue; 
                }
            }
            
            if (string.IsNullOrEmpty(name)) continue;
            // -------------------------------------

            string? matchedSlider = null;

            // 1. Direkte Suche
            matchedSlider = mappings.FirstOrDefault(m => name.Equals(m, StringComparison.OrdinalIgnoreCase)) 
                 ?? mappings.FirstOrDefault(m => name.Contains(m, StringComparison.OrdinalIgnoreCase));

            // 2. Gruppensuche
            if (matchedSlider == null && groups != null)
            {
                var matchedGroup = groups.FirstOrDefault(g => 
                    g.Value.Any(k => name.Equals(k, StringComparison.OrdinalIgnoreCase)));

                if (matchedGroup.Key != null)
                {
                    matchedSlider = matchedGroup.Key;
                }
                else
                {
                    matchedSlider = groups.FirstOrDefault(g => 
                        g.Value.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))).Key;
                }
            }

            if (matchedSlider != null)
            {
                if (!_sessionMap.ContainsKey(matchedSlider)) 
                    _sessionMap[matchedSlider] = new List<IAudioSession>();
                
                _sessionMap[matchedSlider].Add(session);
                
                if (!silent)
                {
                    //LoggerService.Info($"Mapped: {name} -> {matchedSlider}");
                }
            }
        }
    }

    public void SetVolume(string target, float level)
    {
        Task.Run(async () => await SetVolumeAsync(target, level));
    }

    private async Task SetVolumeAsync(string target, float level)
    {
        try 
        {
            int vol = (int)(level * 100);
            var device = _controller.DefaultPlaybackDevice;

            if (target.Equals("master", StringComparison.OrdinalIgnoreCase))
            {
                if (device != null) 
                {
                    await device.SetVolumeAsync(vol);
                }
                return;
            }

            if (_sessionMap.TryGetValue(target, out var sessions))
            {
                foreach (var session in sessions)
                {
                    try 
                    { 
                        await session.SetVolumeAsync(vol); 
                    } 
                    catch 
                    { 
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error($"Fehler beim Setzen der Lautstärke für {target}: {ex.Message}");
        }
    }
    public void PrintDebugMappings()
    {
        LoggerService.Info("=== AKTUELLE MAPPINGS ===");
        
        if (_sessionMap.Count == 0)
        {
            LoggerService.Info("  (Keine Programme zugeordnet)");
        }

        foreach (var entry in _sessionMap)
        {
            string sliderName = entry.Key;     // z.B. "games"
            var sessionList = entry.Value;     // Liste der Sessions (DayZ, Icarus...)
            
            List<string> names = new List<string>();

            foreach (var session in sessionList)
            {
                string name = session.DisplayName;

                // Fallback für DayZ & Co., die keinen DisplayNamen haben
                if (string.IsNullOrEmpty(name))
                {
                    try 
                    { 
                        name = Process.GetProcessById(session.ProcessId).ProcessName + "*"; // Sternchen zeigt an: Name wurde über ID geholt
                    }
                    catch 
                    { 
                        name = $"[PID:{session.ProcessId}]"; 
                    }
                }
                names.Add(name);
            }

            // Ausgabe: "🎚️ games: DayZ*, Icarus*"
            LoggerService.Info($"🎚️ {sliderName}: {string.Join(", ", names)}");
        }
        LoggerService.Info("=========================");
    }
}