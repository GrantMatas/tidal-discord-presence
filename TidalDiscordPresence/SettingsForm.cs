using System.Diagnostics;
using System.Drawing;

namespace TidalDiscordPresence;

internal sealed class SettingsForm : Form
{
    public SettingsForm()
    {
        var config = AppConfig.Load();
        Text = AppBrand.Name;
        Icon = AppBrand.Icon;
        ClientSize = new Size(600, 650);
        MinimumSize = new Size(616, 689);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 10);
        BackColor = Color.FromArgb(245, 247, 250);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1,
            RowCount = 0, AutoScroll = true
        };
        void Add(Control control, int bottom = 10)
        {
            control.Margin = new Padding(0, 0, 0, bottom);
            control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(control, 0, layout.RowCount++);
        }
        Label LabelFor(string text, bool heading = false) => new()
        {
            Text = text, AutoSize = true, MaximumSize = new Size(548, 0),
            ForeColor = Color.FromArgb(28, 36, 48),
            Font = heading ? new Font("Segoe UI Semibold", 11) : Font
        };

        var header = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        header.Controls.Add(new PictureBox { Image = AppBrand.Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Margin = new Padding(0, 0, 14, 0) });
        header.Controls.Add(new Label { Text = "TIDAL Discord Presence", AutoSize = true, Font = new Font("Segoe UI Semibold", 17), Margin = new Padding(0, 7, 0, 0) });
        Add(header, 16);
        Add(LabelFor(string.IsNullOrWhiteSpace(config.DiscordApplicationId)
            ? "Show your TIDAL music on Discord. Add your Discord application below. Lyrics use LRCLIB and need no developer account."
            : "Your saved Discord setup is ready and will be reused. Lyrics come from LRCLIB; no music developer ID, key or account is needed."), 8);
        var portal = new LinkLabel { Text = "Open Discord Developer Portal", AutoSize = true, LinkColor = Color.FromArgb(0, 111, 127) };
        portal.LinkClicked += (_, _) => OpenLink("https://discord.com/developers/applications");
        Add(portal, 16);

        Add(LabelFor(string.IsNullOrWhiteSpace(config.DiscordApplicationId) ? "Discord Application ID" : "Saved Discord Application ID (already configured)", true), 5);
        var id = new TextBox { Text = config.DiscordApplicationId, PlaceholderText = "Paste the numeric Application ID", Width = 548 };
        Add(id, 14);

        Add(LabelFor("Fallback image (blank uses the TIDAL logo)", true), 5);
        var image = new TextBox
        {
            Text = config.ImageAsset == AppConfig.TidalLogoImageUrl ? "" : config.ImageAsset,
            PlaceholderText = "TIDAL logo (default) — or an asset key / HTTPS image URL", Width = 548
        };
        Add(image, 14);

        var artwork = new CheckBox { Text = "Find album covers automatically", Checked = config.EnableArtworkLookup, AutoSize = true };
        Add(artwork, 5);
        Add(LabelFor("Uses Apple, Deezer, and MusicBrainz / Cover Art Archive. When enabled, song metadata is sent to those services. No music account credentials are needed."), 12);
        var lyrics = new CheckBox { Text = "Find synchronized lyrics on LRCLIB (no API key)", Checked = config.EnableLyricsLookup, AutoSize = true };
        Add(lyrics, 5);
        Add(LabelFor("When enabled, song title, artist, album and duration are sent to LRCLIB. Only timed lyrics are used. Discord updates at most every five seconds, so short lines may be skipped."), 12);
        ComboBox Choice(string label, string[] options, string selected)
        {
            Add(LabelFor(label, true), 5);
            var choice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 548 };
            choice.Items.AddRange(options); choice.SelectedItem = selected; Add(choice, 12); return choice;
        }
        var details = Choice("Top profile line (Details)", PresenceText.Fields, config.DetailsField);
        var state = Choice("Second profile line (State)", PresenceText.Fields, config.StateField);
        string[] displayOptions = ["Application name", "State line", "Details line"];
        var display = Choice("Text beside your Discord name", displayOptions, displayOptions[config.StatusDisplayType]);
        var activity = Choice("Activity type", ["Listening", "Playing", "Watching"], config.ActivityType == 0 ? "Playing" : config.ActivityType == 3 ? "Watching" : "Listening");
        Add(LabelFor("Lyric timing adjustment in seconds (positive = earlier)", true), 5);
        var offset = new NumericUpDown { Minimum = -15, Maximum = 15, DecimalPlaces = 1, Increment = .1M, Value = config.LyricOffsetMs / 1000M, Width = 100 };
        Add(offset, 12);
        var preview = LabelFor(""); Add(preview, 14);
        void Preview()
        {
            var sample = new TrackInfo("Song title", "Artist", "Album", true, null, null);
            var d = PresenceText.Select(details.Text, sample, "Current lyric line");
            var s = PresenceText.Select(state.Text, sample, "Current lyric line");
            preview.Text = $"Beside name: {(display.SelectedIndex == 1 ? s : display.SelectedIndex == 2 ? d : "Application name")}\nProfile: {d} | {s}";
        }
        details.SelectedIndexChanged += (_, _) => Preview(); state.SelectedIndexChanged += (_, _) => Preview(); display.SelectedIndexChanged += (_, _) => Preview(); Preview();
        var startup = new CheckBox { Text = "Start with Windows, directly in the system tray", Checked = config.StartWithWindows, AutoSize = true };
        Add(startup, 10);
        var diagnostics = new CheckBox { Text = "Enable local diagnostics for troubleshooting", Checked = config.EnableDiagnostics, AutoSize = true };
        Add(diagnostics, 4);
        Add(LabelFor("Diagnostics store your current song and Discord response locally. Turn this off before sharing the settings folder."), 14);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = new Button { Text = "Save settings", AutoSize = true, Padding = new Padding(10, 4, 10, 4), BackColor = Color.FromArgb(0, 111, 127), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        save.FlatAppearance.BorderSize = 0;
        var cancel = new Button { Text = "Cancel", AutoSize = true, Padding = new Padding(10, 4, 10, 4), DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        Add(buttons, 0);
        save.Click += (_, _) =>
        {
            var value = id.Text.Trim();
            if (value.Length < 16 || value.Length > 22 || !value.All(char.IsAsciiDigit))
            {
                MessageBox.Show(this, "Paste the numeric Application ID from Discord's Developer Portal.", "Application ID required", MessageBoxButtons.OK, MessageBoxIcon.Information);
                id.Focus();
                return;
            }
            var fallback = image.Text.Trim();
            if (fallback.Length > 300 || (fallback.Contains("://") && (!Uri.TryCreate(fallback, UriKind.Absolute, out var uri) || uri.Scheme != "https")))
            {
                MessageBox.Show(this, "Use an HTTPS image URL or a Discord image asset key, up to 300 characters.", "Check fallback image");
                image.Focus();
                return;
            }
            var previousStartup = config.StartWithWindows;
            config.DiscordApplicationId = value;
            config.ImageAsset = string.IsNullOrWhiteSpace(fallback) ? AppConfig.TidalLogoImageUrl : fallback;
            config.EnableArtworkLookup = artwork.Checked;
            config.StartWithWindows = startup.Checked;
            config.EnableDiagnostics = diagnostics.Checked;
            config.EnableLyricsLookup = lyrics.Checked;
            config.DetailsField = details.Text;
            config.StateField = state.Text;
            config.StatusDisplayType = display.SelectedIndex;
            config.ActivityType = activity.SelectedIndex == 1 ? 0 : activity.SelectedIndex == 2 ? 3 : 2;
            config.LyricOffsetMs = (int)(offset.Value * 1000);
            try
            {
                StartupRegistration.SetEnabled(config.StartWithWindows);
                try { config.Save(); }
                catch { StartupRegistration.SetEnabled(previousStartup); throw; }
                if (!config.EnableDiagnostics)
                {
                    var statusPath = Path.Combine(Path.GetDirectoryName(AppConfig.ConfigPath)!, "status.json");
                    try { File.Delete(statusPath); } catch { }
                }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not save settings", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        };
        Controls.Add(layout);
        AcceptButton = save;
        CancelButton = cancel;
    }

    internal static void OpenLink(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
