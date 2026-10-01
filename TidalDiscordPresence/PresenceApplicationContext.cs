using System.Drawing;

namespace TidalDiscordPresence;

internal sealed class PresenceApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _artworkStatus;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly MediaSessionReader _media = new();
    private readonly ArtworkResolver _artwork = new();
    private readonly DiscordRpcClient _discord = new();
    private readonly SemaphoreSlim _updateGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private string? _lastActivityKey;
    private long? _lastStartUnix;
    private DateTime _lastUpdate = DateTime.MinValue;
    private string? _currentArtworkKey;
    private ArtworkMatch? _currentCover;
    private CancellationTokenSource? _artworkLookup;
    private bool _artworkPending;
    private DateTimeOffset _nextArtworkRetry;
    private string? _lastCoverUrl;
    private bool _closing;
    private bool _sharingPaused;
    private SettingsForm? _settings;

    public PresenceApplicationContext()
    {
        var menu = new ContextMenuStrip();
        _statusItem = new ToolStripMenuItem("Waiting for TIDAL") { Enabled = false };
        menu.Items.Add(_statusItem);
        _artworkStatus = new ToolStripMenuItem("Artwork: TIDAL logo") { Enabled = false };
        menu.Items.Add(_artworkStatus);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Settings", null, (_, _) => ShowSettings());
        var pauseSharing = new ToolStripMenuItem("Pause sharing") { CheckOnClick = true };
        pauseSharing.CheckedChanged += (_, _) => { _sharingPaused = pauseSharing.Checked; _ = UpdatePresenceAsync(); };
        menu.Items.Add(pauseSharing);
        menu.Items.Add("Open settings folder", null, (_, _) => SettingsForm.OpenLink(Path.GetDirectoryName(AppConfig.ConfigPath)!));
        menu.Items.Add("About", null, (_, _) => MessageBox.Show($"{AppBrand.Name} {AppBrand.Version}\n\nAn independent Windows companion for TIDAL and Discord.\n\n{AppBrand.RepositoryUrl}", AppBrand.Name, MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add("Uninstall…", null, (_, _) =>
        {
            try { if (Uninstaller.Show()) ExitThread(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Uninstall could not start", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => ExitThread());
        _tray = new NotifyIcon
        {
            Icon = AppBrand.Icon,
            Text = "TIDAL Discord Presence",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();
        _timer = new System.Windows.Forms.Timer { Interval = 2500 };
        _timer.Tick += async (_, _) => await UpdatePresenceAsync();
        _timer.Start();
        _ = UpdatePresenceAsync();
    }

    private void ShowSettings()
    {
        if (_settings is not null) { _settings.Activate(); return; }
        using var settings = new SettingsForm();
        _settings = settings;
        try
        {
            if (settings.ShowDialog() == DialogResult.OK)
            {
                _lastActivityKey = null;
                _ = UpdatePresenceAsync();
            }
        }
        finally { _settings = null; }
    }

    private async Task UpdatePresenceAsync()
    {
        if (_closing || !await _updateGate.WaitAsync(0)) return;
        try
        {
            var config = AppConfig.Load();
            if (string.IsNullOrWhiteSpace(config.DiscordApplicationId))
            {
                SetStatus("Open Settings to add your Discord Application ID");
                return;
            }

            if (_sharingPaused)
            {
                _timer.Interval = 2500;
                _artworkLookup?.Cancel();
                _currentArtworkKey = null;
                if (_lastActivityKey is not null)
                {
                    await _discord.ClearActivityAsync(config.DiscordApplicationId, _shutdown.Token);
                    _lastActivityKey = null;
                }
                SetStatus("Sharing paused");
                return;
            }

            var track = await _media.GetCurrentTidalTrackAsync().WaitAsync(_shutdown.Token);
            if (track is null)
            {
                _timer.Interval = 2500;
                _artworkLookup?.Cancel();
                _currentArtworkKey = null;
                if (_lastActivityKey is not null)
                {
                    await _discord.ClearActivityAsync(config.DiscordApplicationId, _shutdown.Token);
                    _lastActivityKey = null;
                }
                SetStatus("Waiting for TIDAL playback");
                return;
            }

            _timer.Interval = !track.IsPlaying && track.StartUnix.HasValue ? 500 : 2500;
            var key = $"{config.DiscordApplicationId}|{track.Title}|{track.Artist}|{track.Album}|{track.IsPlaying}|{config.ImageAsset}|{config.EnableArtworkLookup}";
            if (!track.IsPlaying) key += $"|{track.PositionSeconds}";
            var artworkKey = $"{track.Title}|{track.Artist}|{track.Album}|{config.EnableArtworkLookup}";
            if (artworkKey != _currentArtworkKey || (config.EnableArtworkLookup && !_artworkPending &&
                _currentCover is null && DateTimeOffset.UtcNow >= _nextArtworkRetry))
            {
                _artworkLookup?.Cancel();
                _artworkLookup?.Dispose();
                _currentArtworkKey = artworkKey;
                _currentCover = null;
                _artworkPending = config.EnableArtworkLookup;
                _nextArtworkRetry = DateTimeOffset.UtcNow.AddMinutes(1);
                if (config.EnableArtworkLookup)
                {
                    _artworkLookup = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
                    _ = ResolveArtworkAsync(track, artworkKey, _artworkLookup.Token);
                }
            }
            var seeked = track.StartUnix.HasValue && _lastStartUnix.HasValue &&
                         Math.Abs(track.StartUnix.Value - _lastStartUnix.Value) > 3;
            var pausedTimestampChanged = !track.IsPlaying && track.StartUnix.HasValue && track.StartUnix != _lastStartUnix;
            if (key != _lastActivityKey || seeked || pausedTimestampChanged || !_discord.IsConnected || _currentCover?.ImageUrl != _lastCoverUrl ||
                DateTime.UtcNow - _lastUpdate > TimeSpan.FromSeconds(15))
            {
                var cover = _currentCover;
                await _discord.SetActivityAsync(config.DiscordApplicationId, track, config.ImageAsset, cover, _shutdown.Token);
                _artworkStatus.Text = _artworkPending ? "Artwork: finding cover…" : cover is null ? "Artwork: fallback image" :
                    $"Artwork: {cover.Source}{(cover.AlbumMatches ? "" : " (alternate release)")}";
                if (config.EnableDiagnostics) SaveArtworkStatus(track, cover, config.ImageAsset);
                _lastActivityKey = key;
                _lastCoverUrl = cover?.ImageUrl;
                _lastStartUnix = track.StartUnix;
                _lastUpdate = DateTime.UtcNow;
            }
            SetStatus($"{(track.IsPlaying ? "Listening" : "Paused")}: {track.Title} — {track.Artist}");
        }
        catch (OperationCanceledException) when (_closing) { }
        catch (Exception ex)
        {
            SetStatus(ex is OperationCanceledException ? "Connection timed out; retrying…" : ex.Message);
        }
        finally { _updateGate.Release(); }
    }

    private async Task ResolveArtworkAsync(TrackInfo track, string key, CancellationToken token)
    {
        try
        {
            var cover = await _artwork.FindAsync(track, token);
            if (_closing || token.IsCancellationRequested || _currentArtworkKey != key) return;
            _currentCover = cover;
            _artworkPending = false;
            _lastUpdate = DateTime.MinValue;
            await UpdatePresenceAsync();
        }
        catch (OperationCanceledException) { }
        catch
        {
            if (!_closing && _currentArtworkKey == key) _artworkPending = false;
        }
    }

    private void SaveArtworkStatus(TrackInfo track, ArtworkMatch? cover, string fallbackImage)
    {
        try
        {
            var path = Path.Combine(Path.GetDirectoryName(AppConfig.ConfigPath)!, "status.json");
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
            {
                updatedUtc = DateTimeOffset.UtcNow,
                track.Title, track.Artist, track.Album,
                track.IsPlaying, track.PositionSeconds,
                artworkSource = cover?.Source ?? "Fallback",
                requestedImage = cover?.ImageUrl ?? fallbackImage,
                discordAccepted = true,
                discordActivity = _discord.LastActivityResponse
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { } // Diagnostic writes must not interrupt playback updates.
    }

    private void SetStatus(string text)
    {
        if (_closing) return;
        _statusItem.Text = text;
        _tray.Text = text.Length > 63 ? text[..60] + "..." : text;
    }

    protected override void ExitThreadCore()
    {
        _closing = true;
        _timer.Stop();
        _timer.Dispose();
        _shutdown.Cancel();
        _artworkLookup?.Cancel();
        _tray.Visible = false;
        _tray.Dispose();
        _discord.Dispose();
        _artwork.Dispose();
        base.ExitThreadCore();
    }
}
