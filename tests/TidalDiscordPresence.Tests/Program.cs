using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Text;
using TidalDiscordPresence;

internal static class Program
{
    private static readonly TrackInfo Track = new("Freak on a Leash", "Korn", "Follow the Leader", true, null, null);
    private const string AppleImage = "https://is1-ssl.mzstatic.com/test/100x100bb.jpg";
    private const string DeezerImage = "https://cdn-images.dzcdn.net/test/cover.jpg";
    private static readonly byte[] Png = CreateImage();

    [STAThread]
    private static async Task Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--render-settings")
        {
            AppConfig.TestDirectory = Path.Combine(Environment.CurrentDirectory, ".build-tools", "screenshot-config");
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            using var form = new SettingsForm { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000) };
            form.Show();
            Application.DoEvents();
            using var image = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(args[1], ImageFormat.Png);
            form.Close();
            return;
        }
        var checks = new (string, Func<Task>)[]
        {
            ("Album selection tries another provider before a compilation", PreferAlbum),
            ("Large image failure retries the smaller image", SmallerImage),
            ("Provider outage still resolves through another provider", ProviderOutage),
            ("No-match cache expires and retries", RetryMissing),
            ("Remixes do not match the original recording", RejectRemix),
            ("Non-image responses are rejected", RejectHtml),
            ("Configuration migration and atomic save preserve settings", Config),
            ("Embedded app icon loads and startup paths are quoted", IconAndStartup)
            ,("Uninstaller only accepts owned files inside the app folder", UninstallPaths)
        };
        foreach (var (name, check) in checks) { await check(); Console.WriteLine("PASS " + name); }
        Console.WriteLine($"Passed {checks.Length} offline regression checks.");
    }

    private static async Task PreferAlbum()
    {
        using var resolver = Resolver(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("itunes.apple.com/search")) return Apple("Greatest Hits, Vol. 1");
            if (url.Contains("api.deezer.com/search")) return Deezer();
            return ImageResponse();
        });
        var match = await resolver.FindAsync(Track, CancellationToken.None);
        Require(match is { Source: "Deezer", AlbumMatches: true }, "A compilation was chosen over the matching album.");
    }

    private static async Task SmallerImage()
    {
        var smallRequested = false;
        using var resolver = Resolver(request =>
        {
            var url = request.RequestUri!.AbsoluteUri;
            if (url.Contains("/search?")) return Apple("Follow the Leader");
            if (url.Contains("600x600")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            smallRequested = true;
            return ImageResponse();
        });
        var probe = await resolver.ProbeAsync(Track, "Apple", CancellationToken.None);
        Require(probe.Match is not null && smallRequested, "Smaller image was not tried.");
    }

    private static async Task ProviderOutage()
    {
        using var resolver = Resolver(request =>
        {
            if (request.RequestUri!.Host == "itunes.apple.com") throw new HttpRequestException("Simulated service outage");
            if (request.RequestUri.Host == "api.deezer.com") return Deezer();
            return ImageResponse();
        });
        var match = await resolver.FindAsync(Track, CancellationToken.None);
        Require(match?.Source == "Deezer", "A provider outage prevented fallback.");
    }

    private static async Task RetryMissing()
    {
        var now = DateTimeOffset.UtcNow;
        var ready = false;
        var requests = 0;
        using var http = Client(request =>
        {
            requests++;
            var host = request.RequestUri!.Host;
            if (host == "itunes.apple.com") return ready ? Apple("Follow the Leader") : Json("{\"results\":[]}");
            if (host == "api.deezer.com") return Json("{\"data\":[]}");
            if (host == "musicbrainz.org") return Json("{\"release-groups\":[]}");
            return ImageResponse();
        });
        using var resolver = new ArtworkResolver(http, () => now);
        Require(await resolver.FindAsync(Track, CancellationToken.None) is null, "Expected no matching cover.");
        var previous = requests;
        ready = true;
        Require(await resolver.FindAsync(Track, CancellationToken.None) is null && requests == previous, "Negative cache was bypassed.");
        now = now.AddMinutes(2);
        Require(await resolver.FindAsync(Track, CancellationToken.None) is not null, "Expired negative cache was never retried.");
    }

    private static async Task RejectRemix()
    {
        using var resolver = Resolver(_ => Apple("Follow the Leader", "Freak on a Leash (Dante Ross Mix)"));
        var probe = await resolver.ProbeAsync(Track, "Apple", CancellationToken.None);
        Require(probe.Match is null, "A remix was accepted for the original recording.");
    }

    private static async Task RejectHtml()
    {
        using var resolver = Resolver(request => request.RequestUri!.Host == "itunes.apple.com"
            ? Apple("Follow the Leader")
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>Not an image</html>") });
        var probe = await resolver.ProbeAsync(Track, "Apple", CancellationToken.None);
        Require(probe.Match is null, "HTML was accepted as album art.");
    }

    private static Task Config()
    {
        var directory = Path.Combine(Environment.CurrentDirectory, ".build-tools", "test-config", Guid.NewGuid().ToString("N"));
        AppConfig.TestDirectory = directory;
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(AppConfig.ConfigPath, "{\"DiscordApplicationId\":\"12345678901234567\",\"ImageAsset\":\"\",\"EnableArtworkLookup\":true}");
            var config = AppConfig.Load();
            Require(config.ImageAsset == AppConfig.TidalLogoImageUrl && config.EnableArtworkLookup, "Legacy config did not migrate.");
            config.StartWithWindows = true;
            config.Save();
            var saved = AppConfig.Load();
            Require(saved.StartWithWindows && saved.DiscordApplicationId == config.DiscordApplicationId, "Atomic save lost settings.");
            Require(!File.Exists(AppConfig.ConfigPath + ".tmp"), "Temporary configuration file was left behind.");
            Require(!saved.EnableDiagnostics, "Diagnostics must be disabled by default.");
        }
        finally { AppConfig.TestDirectory = null; }
        return Task.CompletedTask;
    }

    private static Task IconAndStartup()
    {
        using var bitmap = AppBrand.Icon.ToBitmap();
        Require(bitmap.Width > 0 && bitmap.Height > 0, "Embedded icon did not load.");
        Require(StartupRegistration.BuildCommand(@"C:\Apps With Spaces\Presence.exe") == "\"C:\\Apps With Spaces\\Presence.exe\" --background",
            "Startup command did not quote the executable path.");
        return Task.CompletedTask;
    }

    private static Task UninstallPaths()
    {
        var root = Path.Combine(Environment.CurrentDirectory, ".build-tools", "uninstall-test");
        Require(Uninstaller.ResolveInstalledFile(root, "assets/app.png").StartsWith(Path.GetFullPath(root)), "Owned asset was not resolved.");
        foreach (var invalid in new[] { "../README.md", "C:/Windows/notepad.exe", "personal.txt" })
        {
            var rejected = false;
            try { Uninstaller.ResolveInstalledFile(root, invalid); }
            catch (IOException) { rejected = true; }
            Require(rejected, "An unsafe uninstall path was accepted: " + invalid);
        }
        return Task.CompletedTask;
    }

    private static ArtworkResolver Resolver(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(Client(handler));
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handler) => new(new Handler(handler));
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Apple(string album, string title = "Freak on a Leash") => Json(System.Text.Json.JsonSerializer.Serialize(new
    {
        results = new[] { new { trackName = title, artistName = "Korn", collectionName = album, artworkUrl100 = AppleImage, trackViewUrl = "https://music.apple.com/test" } }
    }));
    private static HttpResponseMessage Deezer() => Json(System.Text.Json.JsonSerializer.Serialize(new
    {
        data = new[] { new { title = Track.Title, artist = new { name = "Korn" }, album = new { title = Track.Album, cover_xl = DeezerImage }, link = "https://www.deezer.com/track/1" } }
    }));
    private static HttpResponseMessage ImageResponse()
    {
        var content = new ByteArrayContent(Png);
        content.Headers.ContentType = new("image/png");
        return new(HttpStatusCode.OK) { Content = content };
    }
    private static byte[] CreateImage()
    {
        using var bitmap = new Bitmap(64,64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Teal);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = callback(request);
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }
}
