using Windows.Media.Control;

namespace TidalDiscordPresence;

internal sealed record TrackInfo(string Title, string Artist, string Album, bool IsPlaying, long? StartUnix, long? EndUnix);

internal sealed class MediaSessionReader
{
    public async Task<TrackInfo?> GetCurrentTidalTrackAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        var session = manager.GetSessions()
            .Where(s => s.SourceAppUserModelId.Contains("tidal", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(s => s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            .FirstOrDefault();
        if (session is null) return null;

        var properties = await session.TryGetMediaPropertiesAsync();
        if (string.IsNullOrWhiteSpace(properties.Title)) return null;
        var playback = session.GetPlaybackInfo();
        if (playback is null || playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
            return null;
        var isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var title = Clean(properties.Title, 128);
        var artist = Clean(properties.Artist, 128);
        var album = Clean(properties.AlbumTitle, 128);
        long? start = null;
        long? end = null;

        if (isPlaying)
        {
            try
            {
                var timeline = session.GetTimelineProperties();
                var duration = timeline.EndTime - timeline.StartTime;
                var position = timeline.Position - timeline.StartTime;
                var age = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
                if (age > TimeSpan.Zero && age < TimeSpan.FromDays(1)) position += age;
                if (duration > TimeSpan.Zero && position > duration) position = duration;
                if (duration > TimeSpan.Zero && position >= TimeSpan.Zero)
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    start = now - (long)position.TotalSeconds;
                    end = start + (long)duration.TotalSeconds;
                }
            }
            catch { }
        }

        return new TrackInfo(title, artist, album, isPlaying, start, end);
    }

    private static string Clean(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Unknown";
        var result = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return result.Length > maxLength ? result[..(maxLength - 1)] + "…" : result;
    }
}
