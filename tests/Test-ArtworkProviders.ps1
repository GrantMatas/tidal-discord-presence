param(
    [string]$TrackReport,
    [string]$OutputPath = '.build-tools/artwork-provider-results.json'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$assemblyPath = Join-Path $projectRoot 'TidalDiscordPresence/bin/Release/net10.0-windows10.0.19041.0/win-x64/TidalDiscordPresence.dll'
$assembly = [System.Reflection.Assembly]::LoadFrom($assemblyPath)
$trackType = $assembly.GetType('TidalDiscordPresence.TrackInfo')
$resolverType = $assembly.GetType('TidalDiscordPresence.ArtworkResolver')
$samples = @([pscustomobject]@{Title='Freak on a Leash'; Artist='Korn'; Album='Follow the Leader'})
if ($TrackReport) {
    $live = (Get-Content -LiteralPath $TrackReport -Raw | ConvertFrom-Json).track
    if ($live) { $samples += $live }
}
$results = @()
foreach ($sample in $samples) {
    $track = [Activator]::CreateInstance($trackType, [object[]]@($sample.Title, $sample.Artist, $sample.Album, $true, $null, $null))
    $resolver = [Activator]::CreateInstance($resolverType, $true)
    try {
        foreach ($source in @('Apple', 'Deezer', 'MusicBrainz')) {
            $task = $resolverType.GetMethod('ProbeAsync').Invoke($resolver, [object[]]@($track, $source, [System.Threading.CancellationToken]::None))
            $probe = $task.GetAwaiter().GetResult()
            $results += [pscustomobject]@{
                Title = $sample.Title
                Artist = $sample.Artist
                Album = $sample.Album
                Source = $source
                ImageFetchedAndDecoded = [bool]$probe.Match
                AlbumMatches = if ($probe.Match) { $probe.Match.AlbumMatches } else { $false }
                ImageUrl = if ($probe.Match) { $probe.Match.ImageUrl } else { $null }
                Error = $probe.Error
            }
        }
        $task = $resolverType.GetMethod('FindAsync').Invoke($resolver, [object[]]@($track, [System.Threading.CancellationToken]::None))
        $match = $task.GetAwaiter().GetResult()
        $results += [pscustomobject]@{
            Title = $sample.Title
            Artist = $sample.Artist
            Album = $sample.Album
            Source = 'Automatic selection'
            ImageFetchedAndDecoded = [bool]$match
            AlbumMatches = if ($match) { $match.AlbumMatches } else { $false }
            ImageUrl = if ($match) { $match.ImageUrl } else { $null }
            SelectedSource = if ($match) { $match.Source } else { $null }
        }
    } finally { $resolver.Dispose() }
}
$reportPath = Join-Path $projectRoot $OutputPath
New-Item -ItemType Directory -Path (Split-Path $reportPath -Parent) -Force | Out-Null
$results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding utf8
$results | Select-Object Title,Source,ImageFetchedAndDecoded,AlbumMatches,SelectedSource,Error | Format-Table -AutoSize
if ($results | Where-Object { $_.Source -eq 'Automatic selection' -and -not $_.ImageFetchedAndDecoded }) {
    throw 'A sample could not resolve artwork from any source.'
}
