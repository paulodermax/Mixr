namespace Mixr_App.Services;

/// <summary>Sucht Katalogeinträge anhand des Session-/Fader-Tokens (Name, AssignmentToken, Key, <c>app:…:Token</c>).</summary>
public static class CatalogGameEntryLookup
{
    public static CatalogGameEntry? FindEntry(GameCatalogStore store, string token)
    {
        foreach (var g in store.Games)
        {
            if (g.Name.Equals(token, StringComparison.OrdinalIgnoreCase))
                return g;
            if (!string.IsNullOrEmpty(g.AssignmentToken) &&
                g.AssignmentToken.Equals(token, StringComparison.OrdinalIgnoreCase))
                return g;
            if (!string.IsNullOrEmpty(g.Key) && g.Key.Equals(token, StringComparison.OrdinalIgnoreCase))
                return g;
            if (CatalogKeyEndsWithToken(g.Key, token))
                return g;
        }

        return null;
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
