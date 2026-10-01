param(
    [string]$Dotnet = 'dotnet',
    [string]$NuGetConfig,
    [switch]$NoRestore
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'TidalDiscordPresence/TidalDiscordPresence.csproj'
$version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$publish = Join-Path $root 'artifacts/publish'
$package = Join-Path $root "artifacts/package/$version"
$release = Join-Path $root 'artifacts/release'
$arguments = @('publish', $project, '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $publish, '--nologo')
if ($NuGetConfig) { $arguments += "-p:RestoreConfigFile=$NuGetConfig" }
if ($NoRestore) { $arguments += '--no-restore' }
& $Dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
New-Item -ItemType Directory -Path $package,$release,(Join-Path $package 'assets') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $publish 'TidalDiscordPresence.exe') -Destination $package -Force
foreach ($file in @('README.md','LICENSE','CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $package -Force
}
Copy-Item -LiteralPath (Join-Path $root 'assets/app.png') -Destination (Join-Path $package 'assets/app.png') -Force
Copy-Item -LiteralPath (Join-Path $root 'tools/Uninstall.cmd') -Destination (Join-Path $package 'Uninstall.cmd') -Force
if (Test-Path -LiteralPath (Join-Path $root 'assets/settings.png')) {
    Copy-Item -LiteralPath (Join-Path $root 'assets/settings.png') -Destination (Join-Path $package 'assets/settings.png') -Force
}
$zipPath = Join-Path $release "TidalDiscordPresence-$version-win-x64.zip"
New-Item -ItemType Directory -Path (Join-Path $package 'assets') -Force | Out-Null
$installedPaths = @('TidalDiscordPresence.exe','Uninstall.cmd','README.md','LICENSE','CHANGELOG.md','assets/app.png')
if (Test-Path -LiteralPath (Join-Path $package 'assets/settings.png')) { $installedPaths += 'assets/settings.png' }
$installedFiles = foreach ($relative in $installedPaths) {
    [pscustomobject]@{ Path = $relative; Sha256 = (Get-FileHash -LiteralPath (Join-Path $package $relative) -Algorithm SHA256).Hash }
}
[pscustomobject]@{ Product = 'TIDAL Discord Presence'; Version = $version; Files = @($installedFiles) } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $package 'install-manifest.json') -Encoding utf8
# Package an explicit allowlist; local settings and diagnostic outputs cannot enter the ZIP.
Add-Type -AssemblyName System.IO.Compression
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
$archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in @($installedPaths) + 'install-manifest.json') {
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $package $relative), $relative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $archive.Dispose() }
$exePath = Join-Path $release 'TidalDiscordPresence.exe'
Copy-Item -LiteralPath (Join-Path $publish 'TidalDiscordPresence.exe') -Destination $exePath -Force
$checksums = foreach ($path in @($exePath,$zipPath)) {
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([IO.Path]::GetFileName($path))"
}
$checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Get-ChildItem -LiteralPath $release -File | Select-Object Name,Length
