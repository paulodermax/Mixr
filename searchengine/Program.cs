using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

const string TwitchTokenUrl = "https://id.twitch.tv/oauth2/token";
const string IgdbGamesUrl = "https://api.igdb.com/v4/games";
const string IgdbCoversUrl = "https://api.igdb.com/v4/covers";

static void PrintUsage()
{
    Console.Error.WriteLine(
        """
        Mixr.SearchEngine — IGDB-Suche (Twitch Client Credentials)

        Umgebungsvariablen (erforderlich):
          IGDB_CLIENT_ID      Twitch Developer Client-ID
          IGDB_CLIENT_SECRET  Twitch Developer Client Secret

        Aufruf:
          Mixr.SearchEngine <Suchbegriff> [--limit N] [--covers]

        Beispiele:
          $env:IGDB_CLIENT_ID="..."; $env:IGDB_CLIENT_SECRET="..."
          dotnet run --project searchengine -- zelda
          dotnet run --project searchengine -- "half life" --limit 3 --covers
        """);
}

var argv = args.ToList();
var withCovers = argv.RemoveAll(a => a is "--covers" or "-c") > 0;
var limit = 10;
var limitIdx = argv.IndexOf("--limit");
if (limitIdx >= 0 && limitIdx + 1 < argv.Count && int.TryParse(argv[limitIdx + 1], out var lim) && lim > 0)
{
    limit = Math.Min(lim, 500);
    argv.RemoveRange(limitIdx, 2);
}

var query = string.Join(" ", argv).Trim();
if (query.Length == 0)
{
    PrintUsage();
    Environment.ExitCode = 1;
    return;
}

var clientId = Environment.GetEnvironmentVariable("IGDB_CLIENT_ID");
var clientSecret = Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET");
if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
{
    Console.Error.WriteLine("Fehler: IGDB_CLIENT_ID und IGDB_CLIENT_SECRET setzen.");
    PrintUsage();
    Environment.ExitCode = 2;
    return;
}

using var http = new HttpClient();

var token = await RequestTokenAsync(http, clientId, clientSecret);
if (token is null)
{
    Environment.ExitCode = 3;
    return;
}

var games = await SearchGamesAsync(http, clientId, token, query, limit);
if (games is null)
{
    Environment.ExitCode = 4;
    return;
}

if (games.Count == 0)
{
    Console.WriteLine("(keine Treffer)");
    return;
}

IReadOnlyList<CoverRow>? coverRows = null;
if (withCovers)
{
    var ids = games.Select(g => g.Id).ToArray();
    coverRows = await FetchCoversAsync(http, clientId, token, ids);
}

if (withCovers)
    Console.WriteLine("id\tname\tslug\trelease\tcover_url");
else
    Console.WriteLine("id\tname\tslug\trelease");

foreach (var g in games)
{
    var line = $"{g.Id}\t{g.Name}\t{g.Slug}\t{FormatUnixDate(g.FirstReleaseDate)}";
    if (withCovers && coverRows is not null)
    {
        var cover = coverRows.FirstOrDefault(c => c.Game == g.Id);
        var url = cover is not null ? BuildCoverImageUrl(cover.ImageId) : "";
        line += $"\t{url}";
    }
    Console.WriteLine(line);
}

static string FormatUnixDate(long? unixSeconds)
{
    if (unixSeconds is null or 0)
        return "";
    try
    {
        return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).UtcDateTime.ToString("yyyy-MM-dd");
    }
    catch
    {
        return unixSeconds.ToString() ?? "";
    }
}

static string BuildCoverImageUrl(string imageId)
{
    if (string.IsNullOrWhiteSpace(imageId))
        return "";
    // IGDB / Cloudinary: https://api-docs.igdb.com/#images
    return $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
}

static async Task<string?> RequestTokenAsync(HttpClient http, string clientId, string clientSecret)
{
    var uri =
        $"{TwitchTokenUrl}?client_id={Uri.EscapeDataString(clientId)}&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";
    try
    {
        var resp = await http.PostAsync(uri, null);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"Token-Request fehlgeschlagen: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(json);
            return null;
        }
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Token-Request: " + ex.Message);
        return null;
    }
}

static async Task<List<GameRow>?> SearchGamesAsync(
    HttpClient http,
    string clientId,
    string token,
    string searchQuery,
    int limit)
{
    var body =
        $"""
        fields id, name, slug, first_release_date;
        search "{EscapeApicalypseString(searchQuery)}";
        limit {limit};
        """;

    var req = new HttpRequestMessage(HttpMethod.Post, IgdbGamesUrl);
    req.Headers.TryAddWithoutValidation("Client-ID", clientId);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    req.Content = new StringContent(body, Encoding.UTF8, "text/plain");

    try
    {
        var resp = await http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"IGDB games fehlgeschlagen: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(json);
            return null;
        }

        var list = new List<GameRow>();
        using var doc = JsonDocument.Parse(json);
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            var id = el.GetProperty("id").GetInt64();
            var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            var slug = el.TryGetProperty("slug", out var s) ? s.GetString() ?? "" : "";
            long? frd = null;
            if (el.TryGetProperty("first_release_date", out var f) && f.ValueKind == JsonValueKind.Number)
                frd = f.GetInt64();
            list.Add(new GameRow(id, name, slug, frd));
        }
        return list;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("IGDB games: " + ex.Message);
        return null;
    }
}

static async Task<IReadOnlyList<CoverRow>?> FetchCoversAsync(
    HttpClient http,
    string clientId,
    string token,
    long[] gameIds)
{
    if (gameIds.Length == 0)
        return Array.Empty<CoverRow>();

    var where = string.Join(",", gameIds);
    var body =
        $"""
        fields id, game, image_id, url;
        where game = ({where});
        limit {gameIds.Length};
        """;

    var req = new HttpRequestMessage(HttpMethod.Post, IgdbCoversUrl);
    req.Headers.TryAddWithoutValidation("Client-ID", clientId);
    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    req.Content = new StringContent(body, Encoding.UTF8, "text/plain");

    try
    {
        var resp = await http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            Console.Error.WriteLine($"IGDB covers fehlgeschlagen: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            Console.Error.WriteLine(json);
            return null;
        }

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
    catch (Exception ex)
    {
        Console.Error.WriteLine("IGDB covers: " + ex.Message);
        return null;
    }
}

static string EscapeApicalypseString(string s)
{
    return s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
}

record GameRow(long Id, string Name, string Slug, long? FirstReleaseDate);

record CoverRow(long Id, long Game, string ImageId);
