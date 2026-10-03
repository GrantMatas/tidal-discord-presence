# Changelog

## 1.0.4

- Show the song title above the artist, with album artwork and the playback timer.
- Use the artist for Discord's compact activity status.
- Keep the artist status while paused and mark the song title as paused.

## 1.0.3

- Read the playback position while paused without advancing it with wall-clock time.
- Rebase Discord's timestamps against the paused position once per second, then restore normal timing on resume.
- Update the paused position when seeking while paused.

## 1.0.2

- Include `by <artist>` after the song title.
- Display the album once on the smaller text line and remove duplicate image text.

## 1.0.1

- Show `TIDAL • <song title>` in Discord's compact activity status using the Details display mode.
- Limit the displayed song details to Discord's 128-character field limit.

## 1.0.0

- Windows tray companion with an embedded EXE, window, and tray icon.
- Track, artist, album, playback timer, pause status, and seek updates.
- Album-art fallbacks across Apple/iTunes, Deezer, and MusicBrainz / Cover Art Archive.
- Validated image downloads, smaller-image retries, and TIDAL logo fallback.
- Artwork loading in the background, with obsolete lookups cancelled on song changes.
- Start with Windows directly in the system tray, and pause/resume sharing.
- High DPI settings window, atomic settings writes, and optional local diagnostics.
- Standalone Windows x64 release with .NET 10 LTS included.
- Uninstaller that removes the startup entry, preserves modified files, and optionally removes settings.
- Offline regression tests and optional online provider checks.
