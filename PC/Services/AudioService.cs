using AudioSwitcher.AudioApi;
using AudioSwitcher.AudioApi.CoreAudio;
using AudioSwitcher.AudioApi.Session;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Mixr.Services;

public class AudioService
{
    private readonly CoreAudioController _controller = new CoreAudioController();
    private Dictionary<string, List<IAudioSession>> _sessionMap = new();

    public void RebuildSessionMap(List<string> mappings, Dictionary<string, List<string>> groups)
    {
        // Da die API async ist, wir aber synchron aufgerufen werden, lagern wir das aus
        Task.Run(async () => await RebuildSessionMapAsync(mappings, groups)).Wait();
    }

    private async Task RebuildSessionMapAsync(List<string> mappings, Dictionary<string, List<string>> groups)
    {
        _sessionMap.Clear();

        var device = _controller.DefaultPlaybackDevice;
        if (device == null) return;

        // FIX 1: SessionController über Capability holen (Alpha 5 Änderung)
        var sessionController = device.GetCapability<IAudioSessionController>();
        if (sessionController == null) return;

        // Async Sessions abrufen
        var sessions = await sessionController.ActiveSessionsAsync();

        foreach (var session in sessions)
        {
            if (string.IsNullOrEmpty(session.DisplayName)) continue;

            string? matchedSlider = mappings.FirstOrDefault(m => 
                session.DisplayName.Contains(m, StringComparison.OrdinalIgnoreCase));

            if (matchedSlider == null && groups != null)
            {
                matchedSlider = groups.FirstOrDefault(g => 
                    g.Value.Any(k => session.DisplayName.Contains(k, StringComparison.OrdinalIgnoreCase))).Key;
            }

            if (matchedSlider != null)
            {
                if (!_sessionMap.ContainsKey(matchedSlider)) 
                    _sessionMap[matchedSlider] = new List<IAudioSession>();
                
                _sessionMap[matchedSlider].Add(session);
                LoggerService.Info($"Mapped: {session.DisplayName} -> {matchedSlider}");
            }
        }
    }

    public void SetVolume(string target, float level)
    {
        // Wir starten den Task "Fire & Forget", damit der Slider nicht laggt
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
                    // FIX 2: SetVolumeAsync statt Property nutzen
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
                        // FIX 3: Auch hier SetVolumeAsync nutzen
                        await session.SetVolumeAsync(vol); 
                    } 
                    catch { }
                }
            }
        }
        catch (Exception ex)
        {
            LoggerService.Error($"Fehler beim Setzen der Lautstärke für {target}", ex);
        }
    }
}