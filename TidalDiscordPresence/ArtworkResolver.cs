using System.Drawing;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TidalDiscordPresence;

internal sealed record ArtworkMatch(string ImageUrl, string TrackUrl, string Source, bool AlbumMatches);
internal sealed record ArtworkProbe(string Source, ArtworkMatch? Match, string? Error);
internal sealed record ArtworkCandidate(string[] Images, string TrackUrl, bool AlbumMatches);

internal sealed class ArtworkResolver : IDisposable
{
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string, (ArtworkMatch? Match, DateTimeOffset Expires)> _cache = new();
    private DateTimeOffset _lastMusicBrainzRequest;

    public ArtworkResolver() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(6) }) { }

    internal ArtworkResolver(HttpClient http, Func<DateTimeOffset>? clock = null)
    {
        _http = http;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("TidalDiscordPresence/1.1 (personal Windows desktop client)");
    }

    public async Task<ArtworkMatch?> FindAsync(TrackInfo track, CancellationToken cancellationToken)
    {
        var key = $"{track.Title}|{track.Artist}|{track.Album}";
        if (_cache.TryGetValue(key, out var cached) && cached.Expires > _clock())
            return cached.Match;
        if (track.Artist == "Unknown") return null;
        ArtworkMatch? alternate = null;
        ArtworkMatch? match = null;
        foreach (var source in new[] { "Apple", "Deezer", "MusicBrainz" })
        {
            var probe = await ProbeAsync(track, source, cancellationToken);
            if (probe.Match is null) continue;
            if (probe.Match.AlbumMatches) { match = probe.Match; break; }
            alternate ??= probe.Match;
        }
        match ??= alternate;
        if (_cache.Count >= 100) _cache.Clear();
        _cache[key] = (match, _clock().Add(match is null ? TimeSpan.FromMinutes(1) : TimeSpan.FromHours(6)));
        return match;
    }

    // Shared by the runtime and the targeted provider checks.
    public async Task<ArtworkProbe> ProbeAsync(TrackInfo track, string source, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var candidates = source switch
            {
                "Apple" => await AppleCandidatesAsync(track, timeout.Token),
                "Deezer" => await DeezerCandidatesAsync(track, timeout.Token),
                "MusicBrainz" => await MusicBrainzCandidatesAsync(track, timeout.Token),
                _ => throw new ArgumentException("Unknown artwork source.")
            };
            foreach (var candidate in candidates.OrderByDescending(c => c.AlbumMatches).Take(3))
            {
                foreach (var image in candidate.Images.Distinct())
                {
                    var verified = await VerifyImageAsync(image, timeout.Token);
                    if (verified is not null)
                        return new ArtworkProbe(source, new ArtworkMatch(verified, candidate.TrackUrl, source, candidate.AlbumMatches), null);
                }
            }
            return new ArtworkProbe(source, null, candidates.Count == 0 ? "No matching catalog entry." : "Candidate images could not be loaded.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            return new ArtworkProbe(source, null, ex is OperationCanceledException ? "Source timed out." : ex.Message);
        }
    }

    private async Task<List<ArtworkCandidate>> AppleCandidatesAsync(TrackInfo track, CancellationToken token)
    {
        var query = Uri.EscapeDataString($"{track.Artist} {track.Title}");
        using var json = await GetJsonAsync($"https://itunes.apple.com/search?term={query}&media=music&entity=song&limit=50", token);
        var candidates = new List<ArtworkCandidate>();
        foreach (var item in json.RootElement.GetProperty("results").EnumerateArray())
        {
            if (!Matches(item, "trackName", track.Title) || !Matches(item, "artistName", track.Artist)) continue;
            var image = Text(item, "artworkUrl100");
            var link = Text(item, "trackViewUrl");
            if (!IsHttpsHost(image, "mzstatic.com") || !IsHttpsHost(link, "apple.com")) continue;
            var large = image.Replace("/100x100bb.", "/600x600bb.", StringComparison.Ordinal);
            candidates.Add(new ArtworkCandidate(new[] { large, image }, link, AlbumMatches(Text(item, "collectionName"), track.Album)));
        }
        return candidates;
    }

    private async Task<List<ArtworkCandidate>> DeezerCandidatesAsync(TrackInfo track, CancellationToken token)
    {
        var query = Uri.EscapeDataString($"{track.Artist} {track.Title}");
        using var json = await GetJsonAsync($"https://api.deezer.com/search?q={query}&limit=50", token);
        if (json.RootElement.TryGetProperty("error", out var error)) throw new IOException($"Deezer: {Text(error, "message")}");
        var candidates = new List<ArtworkCandidate>();
        foreach (var item in json.RootElement.GetProperty("data").EnumerateArray())
        {
            if (!Matches(item, "title", track.Title) || !item.TryGetProperty("artist", out var artist) ||
                !Matches(artist, "name", track.Artist) || !item.TryGetProperty("album", out var album)) continue;
            var link = Text(item, "link");
            if (!IsHttpsHost(link, "deezer.com")) continue;
            var images = new[] { Text(album, "cover_xl"), Text(album, "cover_big"), Text(album, "cover_medium") }
                .Where(image => IsHttpsHost(image, "dzcdn.net")).ToArray();
            if (images.Length > 0)
                candidates.Add(new ArtworkCandidate(images, link, AlbumMatches(Text(album, "title"), track.Album)));
        }
        return candidates;
    }

    private async Task<List<ArtworkCandidate>> MusicBrainzCandidatesAsync(TrackInfo track, CancellationToken token)
    {
        if (track.Album == "Unknown") return new List<ArtworkCandidate>();
        var album = BaseAlbum(track.Album);
        var query = Uri.EscapeDataString($"releasegroup:\"{EscapeQuery(album)}\" AND artist:\"{EscapeQuery(track.Artist)}\"");
        using var json = await GetMusicBrainzJsonAsync($"https://musicbrainz.org/ws/2/release-group?query={query}&fmt=json&limit=5", token);
        var candidates = new List<ArtworkCandidate>();
        foreach (var item in json.RootElement.GetProperty("release-groups").EnumerateArray())
        {
            if (!AlbumMatches(Text(item, "title"), track.Album) || !item.TryGetProperty("artist-credit", out var credits) ||
                !credits.EnumerateArray().Any(credit => Matches(credit, "name", track.Artist))) continue;
            if (!Guid.TryParse(Text(item, "id"), out var id)) continue;
            candidates.Add(new ArtworkCandidate(
                new[] { $"https://coverartarchive.org/release-group/{id}/front-500", $"https://coverartarchive.org/release-group/{id}/front-250" },
                $"https://musicbrainz.org/release-group/{id}", true));
        }
        return candidates;
    }

    private async Task<JsonDocument> GetJsonAsync(string url, CancellationToken token)
    {
        using var response = await _http.GetAsync(url, token);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
    }

    private async Task<JsonDocument> GetMusicBrainzJsonAsync(string url, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            var delay = TimeSpan.FromMilliseconds(1100) - (DateTimeOffset.UtcNow - _lastMusicBrainzRequest);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
            _lastMusicBrainzRequest = DateTimeOffset.UtcNow;
            using var response = await _http.GetAsync(url, token);
            if (attempt == 0 && (response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                                response.StatusCode == HttpStatusCode.TooManyRequests))
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                continue;
            }
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        }
    }

    private async Task<string?> VerifyImageAsync(string url, CancellationToken token, int redirectDepth = 0)
    {
        try
        {
            if (!IsImageHost(url)) return null;
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
            if (!response.IsSuccessStatusCode) return null;
            var resolved = response.RequestMessage?.RequestUri;
            if (resolved is null) return null;
            if (resolved.Scheme == "http")
            {
                if (redirectDepth >= 1) return null;
                var secure = new UriBuilder(resolved) { Scheme = "https", Port = -1 }.Uri.AbsoluteUri;
                return await VerifyImageAsync(secure, token, redirectDepth + 1);
            }
            if (!IsImageHost(resolved.AbsoluteUri) || resolved.AbsoluteUri.Length > 300) return null;
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ||
                response.Content.Headers.ContentLength > 4 * 1024 * 1024) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var bytes = new MemoryStream();
            var buffer = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(buffer, token)) > 0)
            {
                if (bytes.Length + count > 4 * 1024 * 1024) return null;
                bytes.Write(buffer, 0, count);
            }
            bytes.Position = 0;
            using var image = Image.FromStream(bytes, useEmbeddedColorManagement: false, validateImageData: true);
            return image.Width is >= 32 and <= 4096 && image.Height is >= 32 and <= 4096 ? resolved.AbsoluteUri : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    private static bool IsImageHost(string url) => new[] { "mzstatic.com", "dzcdn.net", "coverartarchive.org", "archive.org" }
        .Any(host => IsHttpsHost(url, host));

    private static bool IsHttpsHost(string url, string host) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        (uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    private static string Text(JsonElement item, string field) =>
        item.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Matches(JsonElement item, string field, string expected) => Normalize(Text(item, field)) == Normalize(expected);
    private static bool AlbumMatches(string actual, string expected) =>
        expected != "Unknown" && Normalize(BaseAlbum(actual)) == Normalize(BaseAlbum(expected));
    private static string Normalize(string? value) =>
        new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static string BaseAlbum(string value) => Regex.Replace(value,
        @"\s*[\(\[]\s*(?:special|deluxe|expanded|bonus|remaster|anniversary)[^\)\]]*[\)\]]",
        "", RegexOptions.IgnoreCase);
    private static string EscapeQuery(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public void Dispose() => _http.Dispose();
}
