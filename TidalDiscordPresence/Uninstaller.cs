using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;

namespace TidalDiscordPresence;

internal sealed record InstalledFile(string Path, string Sha256);
internal sealed record InstallManifest(string Product, string Version, InstalledFile[] Files);
internal sealed record UninstallPlan(string ExePath, string InstallDirectory, string SettingsDirectory,
    bool RemoveSettings, int ParentProcessId, InstalledFile[] Files);

internal static class Uninstaller
{
    internal static string ResolveInstalledFile(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("Invalid installation file path.");
        var allowed = new[] { "TidalDiscordPresence.exe", "Uninstall.cmd", "README.md", "LICENSE", "CHANGELOG.md", "assets/app.png", "assets/settings.png" };
        if (!allowed.Contains(relative.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
            throw new IOException("Unrecognized installation file.");
        var directory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(directory, StringComparison.OrdinalIgnoreCase)) throw new IOException("Installation path is outside the application folder.");
        return path;
    }

    public static bool Show()
    {
        var exe = Environment.ProcessPath ?? throw new IOException("Could not locate the application.");
        if (FileVersionInfo.GetVersionInfo(exe).ProductName != AppBrand.Name || Path.GetFileName(exe) != "TidalDiscordPresence.exe")
            throw new IOException("Uninstall is supported from the released application executable.");
        var directory = Path.GetDirectoryName(exe)!;
        using var dialog = new Form
        {
            Text = "Uninstall " + AppBrand.Name, Icon = AppBrand.Icon, ClientSize = new Size(550, 250),
            StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, Font = new Font("Segoe UI", 10)
        };
        var description = new Label { Text = "Remove the app and its Windows startup entry? Files from the release that you have modified will be preserved.", Location = new Point(20, 20), Size = new Size(510, 48) };
        var location = new TextBox { Text = directory, ReadOnly = true, Location = new Point(20, 80), Width = 510 };
        var removeSettings = new CheckBox { Text = "Also remove my settings and local diagnostics", AutoSize = true, Location = new Point(20, 128) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(310, 198), Size = new Size(100, 30) };
        var uninstall = new Button { Text = "Uninstall", DialogResult = DialogResult.OK, Location = new Point(430, 198), Size = new Size(100, 30) };
        dialog.Controls.AddRange(new Control[] { description, location, removeSettings, cancel, uninstall });
        dialog.AcceptButton = cancel;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog() != DialogResult.OK) return false;

        var files = new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        static InstalledFile RecordFile(string path)
        {
            using var stream = File.OpenRead(path);
            return new InstalledFile(path, Convert.ToHexString(SHA256.HashData(stream)));
        }
        files[exe] = RecordFile(exe);
        var manifestPath = Path.Combine(directory, "install-manifest.json");
        if (File.Exists(manifestPath))
        {
            var manifest = JsonSerializer.Deserialize<InstallManifest>(File.ReadAllText(manifestPath))
                ?? throw new IOException("Invalid installation manifest.");
            if (manifest.Product != AppBrand.Name) throw new IOException("Installation manifest belongs to another product.");
            foreach (var entry in manifest.Files)
            {
                var path = ResolveInstalledFile(directory, entry.Path);
                if (!File.Exists(path)) continue;
                using var stream = File.OpenRead(path);
                var hash = Convert.ToHexString(SHA256.HashData(stream));
                if (hash.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)) files[path] = new InstalledFile(path, hash);
            }
            files[manifestPath] = RecordFile(manifestPath);
        }
        var settings = Path.GetDirectoryName(AppConfig.ConfigPath)!;
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TidalDiscordPresence");
        if (!Path.GetFullPath(settings).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Settings directory did not match the application's data folder.");
        var plan = new UninstallPlan(exe, directory, settings, removeSettings.Checked, Environment.ProcessId, files.Values.ToArray());
        var work = Path.Combine(Path.GetTempPath(), "TidalDiscordPresence", "Uninstall", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var planPath = Path.Combine(work, "plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(plan));
        using var resource = typeof(Uninstaller).Assembly.GetManifestResourceStream("TidalDiscordPresence.UninstallScript")
            ?? throw new IOException("Uninstall helper is missing.");
        using var reader = new StreamReader(resource);
        var scriptPath = Path.Combine(work, "uninstall.ps1");
        File.WriteAllText(scriptPath, reader.ReadToEnd());
        StartupRegistration.SetEnabled(false);
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = work
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-PlanPath", planPath }) start.ArgumentList.Add(arg);
        using var helper = Process.Start(start) ?? throw new IOException("Could not start the uninstall helper.");
        return true;
    }
}
