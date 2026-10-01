param([Parameter(Mandatory=$true)][string]$PlanPath, [string]$ErrorLogPath)
$ErrorActionPreference = 'Stop'
$failed = $false
function Get-OwnedFileHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
$work = [IO.Path]::GetFullPath((Split-Path $PlanPath -Parent))
try {
    $plan = Get-Content -LiteralPath $PlanPath -Raw | ConvertFrom-Json
    $install = [IO.Path]::GetFullPath($plan.InstallDirectory).TrimEnd([char]'\')
    $exe = [IO.Path]::GetFullPath($plan.ExePath)
    if ([IO.Path]::GetDirectoryName($exe) -ne $install -or [IO.Path]::GetFileName($exe) -ne 'TidalDiscordPresence.exe') {
        throw 'Application path validation failed.'
    }
    if (([Diagnostics.FileVersionInfo]::GetVersionInfo($exe)).ProductName -ne 'TIDAL Discord Presence') {
        throw 'The executable does not belong to this application.'
    }
    $expectedSettings = [IO.Path]::GetFullPath((Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'TidalDiscordPresence'))
    $settings = [IO.Path]::GetFullPath($plan.SettingsDirectory)
    if ($settings -ne $expectedSettings) { throw 'Settings directory validation failed.' }
    $allowed = @('TidalDiscordPresence.exe','Uninstall.cmd','README.md','LICENSE','CHANGELOG.md','assets/app.png','assets/settings.png','install-manifest.json')
    $validatedFiles = @()
    foreach ($entry in $plan.Files) {
        $file = [IO.Path]::GetFullPath($entry.Path)
        if (-not $file.StartsWith($install + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A removal target is outside the application folder.'
        }
        $relative = $file.Substring($install.Length + 1).Replace('\','/')
        if ($allowed -notcontains $relative) { throw 'Unrecognized removal target.' }
        $validatedFiles += [pscustomobject]@{Path=$file; Sha256=$entry.Sha256}
    }
    $parent = Get-Process -Id $plan.ParentProcessId -ErrorAction SilentlyContinue
    if ($parent -and $parent.Path -eq $exe) { $parent.WaitForExit(5000) | Out-Null }
    $apps = Get-Process -Name TidalDiscordPresence -ErrorAction SilentlyContinue
    foreach ($appProcess in $apps) {
        if ($appProcess.Path -eq $exe) { Stop-Process -Id $appProcess.Id -Force; $appProcess.WaitForExit(5000) | Out-Null }
    }
    foreach ($entry in $validatedFiles) {
        if (-not (Test-Path -LiteralPath $entry.Path -PathType Leaf)) { continue }
        if ((Get-OwnedFileHash $entry.Path) -ne $entry.Sha256) { continue }
        for ($attempt=0; ; $attempt++) {
            try { Remove-Item -LiteralPath $entry.Path -Force; break }
            catch { if ($attempt -ge 30) { throw }; Start-Sleep -Milliseconds 250 }
        }
    }
    if ($plan.RemoveSettings -and (Test-Path -LiteralPath $settings)) {
        $attributes = (Get-Item -LiteralPath $settings -Force).Attributes
        if ($attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Linked settings folders must be removed manually.' }
        # The absolute target was verified above to be exactly this app's named AppData folder.
        Remove-Item -LiteralPath $settings -Recurse -Force
    }
    foreach ($folder in @((Join-Path $install 'assets'),$install)) {
        if ((Test-Path -LiteralPath $folder -PathType Container) -and -not (Get-ChildItem -LiteralPath $folder -Force | Select-Object -First 1)) {
            Remove-Item -LiteralPath $folder
        }
    }
} catch {
    $failed = $true
    if ($ErrorLogPath) { $_ | Out-String | Set-Content -LiteralPath $ErrorLogPath }
    else {
        Add-Type -AssemblyName System.Windows.Forms
        [Windows.Forms.MessageBox]::Show("Some files could not be removed." + [Environment]::NewLine + [Environment]::NewLine + $_.Exception.Message, 'TIDAL Discord Presence uninstall') | Out-Null
    }
} finally {
    foreach ($file in @($PlanPath,$PSCommandPath)) {
        $resolved = [IO.Path]::GetFullPath($file)
        if ([IO.Path]::GetDirectoryName($resolved) -eq $work) { Remove-Item -LiteralPath $resolved -Force -ErrorAction SilentlyContinue }
    }
    if ((Test-Path -LiteralPath $work) -and -not (Get-ChildItem -LiteralPath $work -Force | Select-Object -First 1)) {
        Remove-Item -LiteralPath $work -ErrorAction SilentlyContinue
    }
}
if ($failed) { exit 1 }
