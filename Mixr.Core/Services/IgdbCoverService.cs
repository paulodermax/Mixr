using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mixr.Services;

/// <summary>IGDB v4 (Twitch Client Credentials): Cover t_cover_big mit t_cover_small-Fallback.</summary>
public static class IgdbCoverService
{
    const string TwitchTokenUrl = "https://id.twitch.tv/oauth2/token";
    const string IgdbGamesUrl = "https://api.igdb.com/v4/games";
    const string IgdbExternalGamesUrl = "https://api.igdb.com/v4/external_games";
    const string IgdbCoversUrl = "https://api.igdb.com/v4/covers";

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("Mixr/1.0 (Windows; IGDB)");
        c.Timeout = TimeSpan.FromSeconds(60);
        return c;
    }

    /// <summary>
    /// Lädt ein Cover für ein Steam-Spiel nach <paramref name="destinationPath"/>.
    /// Benötigt <c>IGDB_CLIENT_ID</c> und <c>IGDB_CLIENT_SECRET</c>.
    /// </summary>
    public static async Task<bool> TryDownloadSteamCoverAsync(
        string gameName,
        int steamAppId,
        string destinationPath,
        CancellationToken ct)
    {
        var clientId = Environment.GetEnvironmentVariable("IGDB_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET");
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return false;

        var token = await RequestTokenAsync(clientId, clientSecret, ct).ConfigureAwait(false);
        if (token is null)
            return false;

        long? gameId = null;
        var hint = gameName.Trim();
        if (steamAppId > 0)
            gameId = await TryResolveSteamAppToIgdbGameIdAsync(clientId, token, steamAppId, hint, ct)
                .ConfigureAwait(false);

        if (gameId is null && !string.IsNullOrWhiteSpace(hint))
            gameId = await SearchBestGameIdByNameAsync(clientId, token, hint, ct).ConfigureAwait(false);

        if (gameId is null)
            return false;

        var covers = await FetchCoversAsync(clientId, token, [gameId.Value], ct).ConfigureAwait(false);
        var row = covers.FirstOrDefault(c => c.Game == gameId.Value);
        if (string.IsNullOrWhiteSpace(row.ImageId))
            return false;

        var dir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        return await TryDownloadCoverWithFallbackAsync(row.ImageId, destinationPath, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Lädt ein Cover nur per IGDB-Titelsuche (kein Mapping über Steam-App-ID).
    /// Entspricht <see cref="TryDownloadSteamCoverAsync"/> mit <c>steamAppId = 0</c>.
    /// </summary>
    public static Task<bool> TryDownloadCoverByNameAsync(
        string gameName,
        string destinationPath,
        CancellationToken ct) =>
        TryDownloadSteamCoverAsync(gameName, 0, destinationPath, ct);

    static string BuildCoverImageUrl(string imageId) =>
        $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";

    static string BuildCoverImageUrlSmall(string imageId) =>
        $"https://images.igdb.com/igdb/image/upload/t_cover_small/{imageId}.jpg";

    static async Task<bool> TryDownloadCoverWithFallbackAsync(string imageId, string fullPath, CancellationToken ct)
    {
        var primary = BuildCoverImageUrl(imageId);
        if (await TryDownloadImageToFileAsync(primary, fullPath, ct).ConfigureAwait(false))
            return true;

        var fallback = BuildCoverImageUrlSmall(imageId);
        if (string.Equals(primary, fallback, StringComparison.Ordinal))
            return false;

        return await TryDownloadImageToFileAsync(fallback, fullPath, ct).ConfigureAwait(false);
    }

    static async Task<bool> TryDownloadImageToFileAsync(string url, string fullPath, CancellationToken ct)
    {
        try
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return false;

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0)
                return false;

            await File.WriteAllBytesAsync(fullPath, bytes, ct).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static async Task<string?> RequestTokenAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        var uri =
            $"{TwitchTokenUrl}?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";
        try
        {
            using var resp = await Http.PostAsync(uri, null, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }
        catch
        {
            return null;
        }
    }

    static async Task<long?> TryResolveSteamAppToIgdbGameIdAsync(
        string clientId,
        string token,
        int steamAppId,
        string localNameHint,
        CancellationToken ct)
    {
        var body =
            $"""
            fields game, uid, category;
            where category = 1 & uid = "{steamAppId}";
            limit 10;
            """;

        var req = new HttpRequestMessage(HttpMethod.Post, IgdbExternalGamesUrl);
        req.Headers.TryAddWithoutValidation("Client-ID", clientId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(body, Encoding.UTF8, "text/plain");

        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;

            var gameIds = new List<long>();
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("game", out var g) && g.ValueKind == JsonValueKind.Number)
                    gameIds.Add(g.GetInt64());
            }

            if (gameIds.Count == 0)
                return null;
            if (gameIds.Count == 1)
                return gameIds[0];

            var where = string.Join(",", gameIds.Distinct());
            var gamesBody =
                $"""
                fields id, name;
                where id = ({where});
                limit {gameIds.Count};
                """;
            var named = await PostGamesListAsync(clientId, token, gamesBody, ct).ConfigureAwait(false);
            if (named.Count == 0)
                return gameIds[0];

            return string.IsNullOrWhiteSpace(localNameHint)
                ? gameIds[0]
                : PickBestGameId(localNameHint, named);
        }
        catch
        {
            /* */
        }

        return null;
    }

    static async Task<long?> TryExactNameGameIdAsync(string clientId, string token, string name, CancellationToken ct)
    {
        var body =
            $"""
            fields id, name;
            where name = "{EscapeApicalypseString(name)}";
            limit 5;
            """;

        return await PostGamesFirstIdAsync(clientId, token, body, ct).ConfigureAwait(false);
    }

    static async Task<long?> SearchBestGameIdByNameAsync(string clientId, string token, string name, CancellationToken ct)
    {
        if (name.Length < 2)
            return null;

        var exact = await TryExactNameGameIdAsync(clientId, token, name, ct).ConfigureAwait(false);
        if (exact is not null)
            return exact;

        var body = name.Length >= 4
            ? $"""
              fields id, name;
              search "{EscapeApicalypseString(name)}";
              limit 30;
              """
            : $"""
              fields id, name;
              where name ~ *"{EscapeApicalypseString(name)}"*;
              limit 30;
              """;

        var candidates = await PostGamesListAsync(clientId, token, body, ct).ConfigureAwait(false);
        if (candidates.Count == 0)
            return null;

        return PickBestGameId(name, candidates);
    }

    static async Task<long?> PostGamesFirstIdAsync(string clientId, string token, string body, CancellationToken ct)
    {
        var list = await PostGamesListAsync(clientId, token, body, ct).ConfigureAwait(false);
        return list.Count > 0 ? list[0].Id : null;
    }

    static async Task<List<(long Id, string Name)>> PostGamesListAsync(
        string clientId,
        string token,
        string body,
        CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, IgdbGamesUrl);
        req.Headers.TryAddWithoutValidation("Client-ID", clientId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(body, Encoding.UTF8, "text/plain");

        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return new List<(long, string)>();

            return ParseGamesList(json);
        }
        catch
        {
            return new List<(long, string)>();
        }
    }

    static List<(long Id, string Name)> ParseGamesList(string json)
    {
        var list = new List<(long Id, string Name)>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (!el.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                continue;
            var n = el.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            list.Add((idEl.GetInt64(), n));
        }

        return list;
    }

    static long? PickBestGameId(string localName, List<(long Id, string Name)> candidates)
    {
        var seen = new HashSet<long>();
        var unique = new List<(long Id, string Name)>();
        foreach (var c in candidates)
        {
            if (!seen.Add(c.Id))
                continue;
            unique.Add(c);
        }

        if (unique.Count == 0)
            return null;

        var best = unique[0];
        var bestScore = ScoreNameMatch(localName, best.Name);
        for (var i = 1; i < unique.Count; i++)
        {
            var c = unique[i];
            var s = ScoreNameMatch(localName, c.Name);
            if (s > bestScore || (s == bestScore && c.Name.Length < best.Name.Length))
            {
                bestScore = s;
                best = c;
            }
        }

        return best.Id;
    }

    static readonly Regex WordSplit = new(@"[\W_]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    static string[] TokenizeTitle(string s) =>
        WordSplit.Split(NormalizeTitle(s))
            .Where(t => t.Length > 0)
            .ToArray();

    static string NormalizeTitle(string s) =>
        s.Trim()
            .Replace("™", "", StringComparison.Ordinal)
            .Replace("®", "", StringComparison.Ordinal);

    static int ScoreNameMatch(string localRaw, string igdbRaw)
    {
        var local = NormalizeTitle(localRaw);
        var igdb = NormalizeTitle(igdbRaw);
        if (local.Length == 0 || igdb.Length == 0)
            return -1_000_000;

        if (string.Equals(local, igdb, StringComparison.OrdinalIgnoreCase))
            return 2_000_000;

        var lw = TokenizeTitle(local);
        var gw = TokenizeTitle(igdb);
        if (lw.Length == 0)
            return -1_000_000;

        if (lw.Length == gw.Length && lw.SequenceEqual(gw, StringComparer.OrdinalIgnoreCase))
            return 2_000_000;

        if (gw.Length >= lw.Length)
        {
            var prefixOk = true;
            for (var i = 0; i < lw.Length; i++)
            {
                if (i >= gw.Length || !string.Equals(lw[i], gw[i], StringComparison.OrdinalIgnoreCase))
                {
                    prefixOk = false;
                    break;
                }
            }

            if (prefixOk)
            {
                var extraWords = gw.Length - lw.Length;
                return 1_500_000 - extraWords * 80_000 - igdb.Length;
            }
        }

        if (igdb.Contains(local, StringComparison.OrdinalIgnoreCase) && local.Length >= 4)
            return 400_000 - (igdb.Length - local.Length);

        var sub = ScoreSubsequenceWordMatch(lw, gw);
        if (sub > 0)
            return sub;

        return 100_000 - LevenshteinDistance(local, igdb, cap: 40);
    }

    static int ScoreSubsequenceWordMatch(string[] lw, string[] gw)
    {
        var gi = 0;
        var matched = 0;
        foreach (var w in lw)
        {
            while (gi < gw.Length && !string.Equals(gw[gi], w, StringComparison.OrdinalIgnoreCase))
                gi++;
            if (gi >= gw.Length)
                return 0;
            matched++;
            gi++;
        }

        if (matched != lw.Length)
            return 0;

        var extra = gw.Length - lw.Length;
        return 600_000 - extra * 60_000;
    }

    static int LevenshteinDistance(string a, string b, int cap)
    {
        if (a.Length > cap)
            a = a[..cap];
        if (b.Length > cap)
            b = b[..cap];
        var n = a.Length;
        var m = b.Length;
        if (n == 0)
            return m;
        if (m == 0)
            return n;
        var prev = new int[m + 1];
        var curr = new int[m + 1];
        for (var j = 0; j <= m; j++)
            prev[j] = j;
        for (var i = 1; i <= n; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= m; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[m];
    }

    static async Task<IReadOnlyList<CoverRow>> FetchCoversAsync(
        string clientId,
        string token,
        long[] gameIds,
        CancellationToken ct)
    {
        if (gameIds.Length == 0)
            return Array.Empty<CoverRow>();

        var where = string.Join(",", gameIds.Distinct());
        var limit = Math.Min(gameIds.Distinct().Count(), 500);
        var body =
            $"""
            fields id, game, image_id, url;
            where game = ({where});
            limit {limit};
            """;

        var req = new HttpRequestMessage(HttpMethod.Post, IgdbCoversUrl);
        req.Headers.TryAddWithoutValidation("Client-ID", clientId);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Content = new StringContent(body, Encoding.UTF8, "text/plain");

        try
        {
            using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return Array.Empty<CoverRow>();

            var list = new List<CoverRow>();
            using var doc = JsonDocument.Parse(json);
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = el.GetProperty("id").GetInt64();
                var game = el.GetProperty("game").GetInt64();
                var imageId = "";
                if (el.TryGetProperty("image_id", out var img))
                {
                    imageId = img.ValueKind switch
                    {
                        JsonValueKind.String => img.GetString() ?? "",
                        JsonValueKind.Number => img.GetInt64().ToString(),
                        _ => "",
                    };
                }

                list.Add(new CoverRow(id, game, imageId));
            }

            return list;
        }
        catch
        {
            return Array.Empty<CoverRow>();
        }
    }

    static string EscapeApicalypseString(string s) =>
        s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    readonly record struct CoverRow(long Id, long Game, string ImageId);
}
