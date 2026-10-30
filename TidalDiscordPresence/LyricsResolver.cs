using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TidalDiscordPresence;

internal sealed class LyricTimeline
{
    private readonly List<(long Time, string Text)> _lines = new();
    public int Count => _lines.Count;
    public static LyricTimeline Parse(string? lrc)
    {
        var result = new LyricTimeline();
        long offset = 0;
        foreach (var raw in (lrc ?? "").Split('\n'))
        {
            var shift = Regex.Match(raw, @"\[offset:([+-]?\d+)\]", RegexOptions.IgnoreCase);
            if (shift.Success) long.TryParse(shift.Groups[1].Value, out offset);
            var text = Regex.Replace(raw, @"\[[^\]]*\]", "").Trim();
            foreach (Match tag in Regex.Matches(raw, @"\[(\d{1,4}):(\d{2})(?:[.:](\d{1,3}))?\]"))
            {
                var seconds = int.Parse(tag.Groups[2].Value);
                if (seconds >= 60) continue;
                var time = long.Parse(tag.Groups[1].Value) * 60000 + seconds * 1000 + int.Parse(tag.Groups[3].Value.PadRight(3, '0'));
                result._lines.Add((time, text));
            }
        }
        for (var i = 0; i < result._lines.Count; i++) result._lines[i] = (Math.Max(0, result._lines[i].Time - offset), result._lines[i].Text);
        result._lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return result;
    }
    public string At(long position) => _lines.LastOrDefault(line => line.Time <= position).Text ?? "";
}

internal sealed class LyricsResolver : IDisposable
{
    private readonly HttpClient _http;
    private DateTimeOffset _retryAfter;
    public LyricsResolver(HttpMessageHandler? handler = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(12);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"TidalDiscordPresence/{AppBrand.Version} (+{AppBrand.RepositoryUrl})");
    }
    public async Task<LyricTimeline> FindAsync(TrackInfo track, CancellationToken token)
    {
        if (DateTimeOffset.UtcNow < _retryAfter) throw new HttpRequestException("Lyrics service is cooling down.");
        var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(track.Title) + "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (track.Album != "Unknown") url += "&album_name=" + Uri.EscapeDataString(track.Album);
        var duration = track.EndUnix - track.StartUnix;
        if (duration is >= 1 and <= 3600) url += "&duration=" + duration.Value.ToString(CultureInfo.InvariantCulture);
        using var response = await _http.GetAsync(url, token);
        if (response.StatusCode == HttpStatusCode.NotFound) return new LyricTimeline();
        if ((int)response.StatusCode is 429 or 503)
        {
            _retryAfter = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(1));
            throw new HttpRequestException("Lyrics service is busy; retrying later.");
        }
        response.EnsureSuccessStatusCode();
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(token), cancellationToken: token);
        return LyricTimeline.Parse(json.RootElement.TryGetProperty("syncedLyrics", out var value) ? value.GetString() : null);
    }
    public void Dispose() => _http.Dispose();
}

internal static class PresenceText
{
    public static readonly string[] Fields = ["Current lyric", "Song title", "Artist", "Album", "Song + artist", "None"];
    public static string Select(string field, TrackInfo track, string? lyric) => field switch
    {
        "Current lyric" => track.IsPlaying ? lyric ?? "" : "Paused",
        "Song title" => track.IsPlaying ? track.Title : $"Paused • {track.Title}",
        "Artist" => track.Artist,
        "Album" => track.Album,
        "Song + artist" => $"{track.Title} · {track.Artist}",
        _ => ""
    };
}
