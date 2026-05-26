using System.Text;

namespace Mixr_App.Services;

/// <summary>Sucht Katalogeinträge anhand des Session-/Fader-Tokens (Name, AssignmentToken, Key, Fuzzy).</summary>
public static class CatalogGameEntryLookup
{
    public static CatalogGameEntry? FindEntry(GameCatalogStore store, string token) =>
        FindBest(store, token);

    public static CatalogGameEntry? FindBest(GameCatalogStore store, string tokenOrLabel)
    {
        if (string.IsNullOrWhiteSpace(tokenOrLabel))
            return null;

        var raw = tokenOrLabel.Trim();
        foreach (var g in store.Games)
        {
            if (g.Name.Equals(raw, StringComparison.OrdinalIgnoreCase))
                return g;
            if (!string.IsNullOrEmpty(g.AssignmentToken) &&
                g.AssignmentToken.Equals(raw, StringComparison.OrdinalIgnoreCase))
                return g;
            if (!string.IsNullOrEmpty(g.Key) && g.Key.Equals(raw, StringComparison.OrdinalIgnoreCase))
                return g;
            if (CatalogKeyEndsWithToken(g.Key, raw))
                return g;
        }

        var norm = NormalizeSessionLabel(raw);
        if (norm.Length > 0)
        {
            foreach (var g in store.Games)
            {
                if (g.Name.Equals(norm, StringComparison.OrdinalIgnoreCase))
                    return g;
                if (!string.IsNullOrEmpty(g.AssignmentToken) &&
                    g.AssignmentToken.Equals(norm, StringComparison.OrdinalIgnoreCase))
                    return g;
            }
        }

        var needle = Slug(norm.Length > 0 ? norm : raw);
        if (needle.Length < 2)
            return null;

        CatalogGameEntry? best = null;
        var bestScore = 0;

        foreach (var g in store.Games)
        {
            foreach (var candidate in CandidateStrings(g))
            {
                var hay = Slug(candidate);
                if (hay.Length < 2)
                    continue;

                var score = ScoreSlugMatch(needle, hay, candidate, raw);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = g;
                }
            }
        }

        return bestScore >= 60 ? best : null;
    }

    static IEnumerable<string> CandidateStrings(CatalogGameEntry g)
    {
        if (!string.IsNullOrEmpty(g.Name))
            yield return g.Name;
        if (!string.IsNullOrEmpty(g.AssignmentToken))
            yield return g.AssignmentToken;
    }

    static int ScoreSlugMatch(string needle, string hay, string original, string raw)
    {
        if (hay.Equals(needle, StringComparison.OrdinalIgnoreCase))
            return 100;
        if (original.Equals(raw, StringComparison.OrdinalIgnoreCase))
            return 95;
        if (hay.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
            needle.Contains(hay, StringComparison.OrdinalIgnoreCase))
            return 75;
        return 0;
    }

    static string NormalizeSessionLabel(string label)
    {
        var s = label.Trim();
        if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            s = s[..^4].Trim();
        return s;
    }

    static string Slug(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s.Trim())
        {
            if (char.IsAsciiLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    static bool CatalogKeyEndsWithToken(string? key, string token)
    {
        if (string.IsNullOrEmpty(key))
            return false;
        var idx = key.LastIndexOf(':');
        if (idx < 0 || idx >= key.Length - 1)
            return false;
        var tail = key[(idx + 1)..];
        return tail.Equals(token, StringComparison.OrdinalIgnoreCase);
    }
}
