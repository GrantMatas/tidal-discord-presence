using System.Drawing;

namespace TidalDiscordPresence;

internal static class AppBrand
{
    public const string Name = "TIDAL Discord Presence";
    public const string RepositoryUrl = "https://github.com/GrantMatas/tidal-discord-presence";
    public const string Version = "1.0.0";
    public static readonly Icon Icon = LoadIcon();

    private static Icon LoadIcon()
    {
        using var stream = typeof(AppBrand).Assembly.GetManifestResourceStream("TidalDiscordPresence.AppIcon")
            ?? throw new InvalidOperationException("Application icon is missing.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }
}
