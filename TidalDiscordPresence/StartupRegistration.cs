using Microsoft.Win32;

namespace TidalDiscordPresence;

internal static class StartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "TidalDiscordPresence";

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            using var existing = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (existing?.GetValue(ValueName) is null) return;
        }
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (enabled)
        {
            var path = Environment.ProcessPath ?? throw new IOException("Could not locate the executable.");
            key.SetValue(ValueName, BuildCommand(path), RegistryValueKind.String);
        }
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    internal static string BuildCommand(string executablePath) => $"\"{executablePath}\" --background";
}
