using Microsoft.Win32;

namespace Mixr.Services;

/// <summary>
/// Liest installierte Programme (Uninstall-Registry) und liefert Suchstrings für session_groups.
/// Reihenfolge der Regeln wichtig (z. B. TeamSpeak vor „Teams“).
/// </summary>
public static class InstalledAppDetector
{
    public readonly record struct Suggestion(string GroupKey, string SearchToken, string MatchedDisplayName);

    public static IReadOnlyList<Suggestion> DetectSuggestions()
    {
        var displayNames = new List<string>();
        foreach (var dn in EnumerateUninstallDisplayNames())
        {
            if (!string.IsNullOrWhiteSpace(dn))
                displayNames.Add(dn);
        }

        var found = new List<Suggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var display in displayNames)
        {
            foreach (var (group, token) in MatchRules(display))
            {
                var key = $"{group}|{token}";
                if (seen.Add(key))
                    found.Add(new Suggestion(group, token, display));
            }
        }

        return found;
    }

    static IEnumerable<string> EnumerateUninstallDisplayNames()
    {
        foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
        {
            foreach (var subPath in UninstallSubKeys)
            {
                using var key = root.OpenSubKey(subPath);
                if (key == null)
                    continue;
                foreach (var name in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(name);
                    var disp = sub?.GetValue("DisplayName") as string;
                    if (!string.IsNullOrWhiteSpace(disp))
                        yield return disp.Trim();
                }
            }
        }
    }

    static readonly string[] UninstallSubKeys =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
    ];

    static IEnumerable<(string Group, string Token)> MatchRules(string display)
    {
        var d = display;

        if (d.Contains("TeamSpeak", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "TeamSpeak");
            yield break;
        }

        if (d.Contains("Discord", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Discord");
            yield break;
        }

        if (d.Contains("Microsoft Teams", StringComparison.OrdinalIgnoreCase) ||
            (d.Contains("Teams", StringComparison.OrdinalIgnoreCase) && d.Contains("Microsoft", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("communication", "Teams");
            yield break;
        }

        if (d.Contains("Google Meet", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Meet");
            yield break;
        }

        if (d.Contains("Zoom", StringComparison.OrdinalIgnoreCase) && !d.Contains("ZoomText", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Zoom");
            yield break;
        }

        if (d.Contains("Slack", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Slack");
            yield break;
        }

        if (d.Contains("Skype", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Skype");
            yield break;
        }

        if (d.Contains("Webex", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Webex");
            yield break;
        }

        if (d.Contains("Signal", StringComparison.OrdinalIgnoreCase) && d.Contains("Desktop", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "Signal");
            yield break;
        }

        if (d.Contains("WhatsApp", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("communication", "WhatsApp");
            yield break;
        }

        if (d.Contains("Google Chrome", StringComparison.OrdinalIgnoreCase) ||
            (d.Contains("Chrome", StringComparison.OrdinalIgnoreCase) && d.Contains("Google", StringComparison.OrdinalIgnoreCase)))
        {
            yield return ("media", "Chrome");
            yield break;
        }

        if (d.Contains("Brave", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "Brave");
            yield break;
        }

        if (d.Contains("Firefox", StringComparison.OrdinalIgnoreCase) || d.Contains("Mozilla", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "Firefox");
            yield break;
        }

        if (d.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "Spotify");
            yield break;
        }

        if (d.Contains("Apple Music", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "Apple Music");
            yield break;
        }

        if (d.Contains("iTunes", StringComparison.OrdinalIgnoreCase) && d.Contains("Apple", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "iTunes");
            yield break;
        }

        if (d.Contains("Vivaldi", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "Vivaldi");
            yield break;
        }

        if (d.Contains("Opera", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "Opera");
            yield break;
        }

        if (d.Contains("Internet Explorer", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("media", "iexplore");
            yield break;
        }

        if (d.Contains("GOG GALAXY", StringComparison.OrdinalIgnoreCase) || d.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("games", "GOG Galaxy");
            yield break;
        }

        if (d.Contains("Ubisoft Connect", StringComparison.OrdinalIgnoreCase) || d.Contains("Uplay", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("games", "UbisoftConnect");
            yield break;
        }

        if (d.Contains("EA app", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(d.Trim(), "Origin", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("games", "EADesktop");
            yield break;
        }

        if (d.Contains("League of Legends", StringComparison.OrdinalIgnoreCase))
        {
            yield return ("games", "League of Legends");
            yield break;
        }
    }
}
