namespace Mixr.Services;

/// <summary>Abgleich Windows-Audio-Session (Anzeigename/Prozess) mit config session_groups-Einträgen.</summary>
public static class SessionTokenMatcher
{
    static readonly Dictionary<string, string[]> ProcessAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TslGame"] = ["pubg", "battlegrounds"],
        ["ExecPubg"] = ["pubg", "battlegrounds"],
        ["TslGame_BE"] = ["pubg", "battlegrounds"],
    };

    public static bool Matches(string sessionName, string configToken)
    {
        if (string.IsNullOrWhiteSpace(sessionName) || string.IsNullOrWhiteSpace(configToken))
            return false;

        var name = sessionName.Trim();
        var token = configToken.Trim();

        if (name.Equals(token, StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            return true;
        if (token.Contains(name, StringComparison.OrdinalIgnoreCase) && name.Length >= 4)
            return true;

        foreach (var part in SplitTokenParts(token))
        {
            if (part.Length >= 3 && name.Contains(part, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var nameSlug = Slug(name);
        var tokenSlug = Slug(token);
        if (nameSlug.Length >= 2 && tokenSlug.Length >= 2)
        {
            if (nameSlug.Contains(tokenSlug, StringComparison.OrdinalIgnoreCase) ||
                tokenSlug.Contains(nameSlug, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        if (ProcessAliases.TryGetValue(name, out var hints))
        {
            foreach (var hint in hints)
            {
                if (hint.Length < 3)
                    continue;
                if (token.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    return true;
                if (tokenSlug.Contains(Slug(hint), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    public static string? MatchToMapping(string sessionName, IReadOnlyList<string> mappings)
    {
        foreach (var m in mappings)
        {
            if (Matches(sessionName, m))
                return m;
        }

        return null;
    }

    public static string? MatchToGroupKey(string sessionName, IReadOnlyDictionary<string, List<string>> groups)
    {
        foreach (var g in groups)
        {
            if (g.Value.Any(token => Matches(sessionName, token)))
                return g.Key;
        }

        return null;
    }

    static IEnumerable<string> SplitTokenParts(string token)
    {
        foreach (var chunk in token.Split(':', '–', '-', '|'))
        {
            var part = chunk.Trim();
            if (part.Length > 0)
                yield return part;
        }
    }

    static string Slug(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }
}
