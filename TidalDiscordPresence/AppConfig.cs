using System.Text.Json;

namespace TidalDiscordPresence;

internal sealed class AppConfig
{
    public const string TidalLogoImageUrl = "https://is1-ssl.mzstatic.com/image/thumb/Purple211/v4/e3/d9/1d/e3d91d92-d0d3-1766-924a-7c0a68f1f77b/AppIcon-0-0-1x_U007epad-0-1-0-85-220.png/512x512bb.jpg";
    public string DiscordApplicationId { get; set; } = "";
    public string ImageAsset { get; set; } = TidalLogoImageUrl;
    public bool EnableArtworkLookup { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public bool EnableDiagnostics { get; set; } = false;
    public bool EnableLyricsLookup { get; set; } = false;
    public string DetailsField { get; set; } = "Song title";
    public string StateField { get; set; } = "Artist";
    public int StatusDisplayType { get; set; } = 1;
    public int ActivityType { get; set; } = 2;
    public int LyricOffsetMs { get; set; }

    internal static string? TestDirectory { get; set; }
    public static string ConfigPath => Path.Combine(TestDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "TidalDiscordPresence"), "config.json");

    public static AppConfig Load()
    {
        EnsureExists();
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppConfig();
            if (string.IsNullOrWhiteSpace(config.ImageAsset)) config.ImageAsset = TidalLogoImageUrl;
            if (!PresenceText.Fields.Contains(config.DetailsField)) config.DetailsField = "Song title";
            if (!PresenceText.Fields.Contains(config.StateField)) config.StateField = "Artist";
            config.StatusDisplayType = Math.Clamp(config.StatusDisplayType, 0, 2);
            if (config.ActivityType is not (0 or 2 or 3)) config.ActivityType = 2;
            config.LyricOffsetMs = Math.Clamp(config.LyricOffsetMs, -15000, 15000);
            return config;
        }
        catch
        {
            return new AppConfig();
        }
    }

    public static void EnsureExists()
    {
        if (File.Exists(ConfigPath)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(new AppConfig(), new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var temporary = ConfigPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, ConfigPath, overwrite: true);
    }
}
