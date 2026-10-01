using System.Windows.Forms;

namespace TidalDiscordPresence;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        try { Run(args); }
        catch (Exception ex)
        {
            MessageBox.Show($"The app could not start.\n\n{ex.Message}", AppBrand.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.ExitCode = 1;
        }
    }

    private static void Run(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--uninstall")) { Uninstaller.Show(); return; }
        if (args.Length == 2 && args[0] == "--diagnose")
        {
            try
            {
                var track = new MediaSessionReader().GetCurrentTidalTrackAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { success = true, track }));
            }
            catch (Exception ex)
            {
                File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { success = false, error = ex.Message }));
                Environment.ExitCode = 1;
            }
            return;
        }
        using var instance = new Mutex(true, "Local\\TidalDiscordPresence", out var created);
        if (!created)
        {
            if (!args.Contains("--background"))
                MessageBox.Show("TIDAL Discord Presence is already running. Double-click its system tray icon to open settings.", AppBrand.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (string.IsNullOrWhiteSpace(AppConfig.Load().DiscordApplicationId))
        {
            if (args.Contains("--background")) return;
            using var setup = new SettingsForm();
            if (setup.ShowDialog() != DialogResult.OK) return;
        }
        Application.Run(new PresenceApplicationContext());
    }
}
