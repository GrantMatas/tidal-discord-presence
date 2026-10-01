# Contributing

Use Windows and the .NET 10 SDK.

1. Fork the repository and make a branch for your change.
2. Build and run the offline checks:

   ```powershell
   dotnet run --project tests/TidalDiscordPresence.Tests/TidalDiscordPresence.Tests.csproj -c Release
   ```

3. Build the standalone executable:

   ```powershell
   ./tools/Build-Release.ps1
   ```

4. Test changes to playback and presence with both desktop clients open. Album-art tests can be run with `tests/Test-ArtworkProviders.ps1` after publishing; they require PowerShell 7.6 or newer and contact external services.
5. Open a pull request describing the behavior changed and checks completed.

Keep configuration, tokens, diagnostic reports, and real listening history out of commits and issue attachments. The automated checks run without music catalogs or Discord; third-party services should not make CI flaky.

`tools/New-AppIcon.ps1` regenerates the multi-resolution ICO and PNG from the app's diamond-and-presence design. `assets/app.svg` is the matching vector source.
