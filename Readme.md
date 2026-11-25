# LRCLIB Lyrics for Emby

An Emby Server plugin that automatically downloads missing synchronised lyrics (`.lrc`) for your music tracks from [LRCLIB](https://lrclib.net), a free, open lyrics database.

Emby already reads sidecar `.lrc` files when it scans a library, but it has no working way to fetch them. This plugin fills that gap by hooking into the normal library scan / metadata refresh: tracks that have no lyrics get a lookup, and the result is saved next to the audio file where Emby picks it up like any other sidecar.

## Features

- **Fully automatic** - runs as part of Emby's own scan and metadata refresh. There is no scheduled task to configure and no manual step.
- **Synced lyrics first** - saves `Track Name.lrc` beside each audio file.
- **Plain-text fallback** - if LRCLIB only has unsynchronised lyrics, optionally saves a `.txt` sidecar and keeps looking for a synced version later.
- **Upgrades plain text to synced** - a track with only a `.txt` sidecar is re-checked, and the `.txt` is moved to the recycle bin once a `.lrc` has been saved.
- **Opt-in by library path** - nothing is touched until you enable a music library path on the config page.
- **Polite to LRCLIB** - sequential requests with a minimum gap, `Retry-After` honoured with jitter, and a circuit breaker on `429` / `401`.
- **Cheap on rescans** - a persistent negative-result cache means tracks with no lyrics on LRCLIB are not queried again on every scan.
- **Non-destructive** - existing sidecars are never overwritten, and tracks with lyrics already embedded in the file are ignored.

## Requirements

- Emby Server 4.9.1.90 or later
- Audio files with title, artist and duration available (album is used when present)
- Write access to your music folders (see [Where lyrics are saved](#where-lyrics-are-saved) for the fallback)

## Installation

1. Build the project (see [Building](#building)) or obtain `GNLrclib.dll`.
2. Copy `GNLrclib.dll` into your Emby Server `plugins` folder.
3. Restart Emby Server.
4. Open **LRCLIB Lyrics** under the server's plugin settings.

## Configuration

Settings are on the plugin's configuration page and save automatically as you change them.

| Setting | Default | Description |
|---|---|---|
| Enable Plugin | On | Master switch. When off, no lyrics are looked up or written. |
| Save plain-text lyrics when no synced lyrics exist | On | Writes a `.txt` sidecar when LRCLIB only has unsynchronised lyrics. The track is re-checked later for a synced version. |
| Replace plain-text lyrics when synced lyrics are found | On | Moves an existing `.txt` sidecar to the recycle bin once a `.lrc` has been saved for the track. |
| Minimum days between checks | 3 | Shortest wait before an unsuccessful track is checked again. |
| Maximum days between checks | 90 | Longest wait before an unsuccessful track is checked again. |
| Library / Path Filter | all off | Music libraries and their folders. Only tracks under an **enabled** path are processed. |

> **Nothing happens until you enable at least one library path.** Only libraries with the `music` content type are listed. A path under a disabled entry is always excluded.

## How it works

### Where it hooks in

The plugin registers a custom metadata provider for audio items. Emby runs custom providers after the media probe (ffprobe), so by the time the plugin runs the track already carries the title, artist(s), album and duration read from its tags. These are exactly the fields LRCLIB needs, which is why the plugin runs there rather than earlier in the refresh.

The provider also implements Emby's item change monitor, which is what makes Emby invoke it on ordinary scans, and the forced-provider marker so it still runs for locked items.

### Deciding whether a track needs a lookup

A single evaluator makes this decision, used both by the cheap pre-refresh change check and by the refresh itself so the two cannot disagree:

```
Is the track a local, saved file inside an enabled library path?      no -> skip
Does it have a title, an artist (album artist as fallback)
and a duration of 1-3600 seconds?                                    no -> skip
Does it have embedded lyrics or a synced .lrc sidecar?               yes -> skip
Has it already been resolved with synced lyrics?                     yes -> skip, permanently
Was it checked before and is the re-check not yet due?               yes -> skip
Otherwise:
   has a .txt sidecar only  -> upgrade to synced
   has no lyrics            -> fetch missing
```

Lyrics are detected from the item's media streams: embedded lyrics appear as a non-external subtitle stream, and sidecar files as external subtitle streams (`.lrc` synced, `.txt` plain text).

### The lookup

The plugin queries LRCLIB's exact-signature endpoint, `GET /api/get`, with track name, artist name, duration and (when present) album name. The outcome is classified as:

| LRCLIB response | Result |
|---|---|
| Synced lyrics present | Save `.lrc` |
| Instrumental flag, no lyrics | Recorded as instrumental |
| Plain lyrics only | Save `.txt` (if enabled) and re-check later |
| `404` or empty record | Recorded as not found |
| `429`, `401`, timeout, network or server error | Transient - nothing recorded, retried on a later scan |

Requests are sent one at a time with a minimum 400 ms gap. A `429` pauses lookups for the `Retry-After` period (60 s if absent) plus up to 30% jitter. A `401` (rejected App ID) pauses lookups for an hour and logs a hint to check the configured App ID.

### Where lyrics are saved

Files are named after the audio file, e.g. `01 - Song.flac` -> `01 - Song.lrc`, and written as UTF-8 without a BOM. The plugin writes next to the audio file first, and falls back to the item's internal Emby metadata folder if the media folder is not writable (Emby searches both). An existing file is never overwritten. File operations are bracketed with Emby's library monitor so the plugin's own writes do not trigger a rescan.

The new sidecar is picked up by Emby's normal probe on a following scan, so lyrics appear after the next refresh of the track rather than instantly.

### Re-checking tracks without lyrics

Every check is recorded in a small JSON ledger keyed by Emby item id, stored at `<Emby data path>/lrclib-lyrics/lyrics-check-ledger.json`. It is flushed a few seconds after the last change and replaced atomically.

| Last outcome | Behaviour |
|---|---|
| Synced | Never looked up again, so a `.lrc` you delete stays deleted. |
| Not found / Instrumental / Plain text only / Write failed | Re-checked once the back-off interval has elapsed. |

The back-off is **half the track's age in your library**, clamped between the configured minimum and maximum. Recently added tracks are likely to gain lyrics on LRCLIB soon so they are re-checked quickly; long-standing tracks rarely do, so they settle at the maximum interval.

## Building

Requires the .NET SDK. The plugin targets `netstandard2.0` and references `MediaBrowser.Server.Core` 4.9.1.90.

```
dotnet build src/GNLrclib/GNLrclib.csproj -c Release
```

The project includes a post-build step that copies the output to `%APPDATA%\Emby-Server\programdata\plugins\` on Windows, and a `launchSettings.json` for debugging with a local Emby Server.

## Project layout

```
src/GNLrclib/
  Plugin.cs                      Plugin entry point and config page registration
  Providers/                     LrcLibLyricsProvider - the Emby metadata provider
  Services/
    LyricsStatusEvaluator.cs     Decides whether a track needs work
    RecheckPolicy.cs             Age-based re-check back-off
    LyricsCheckLedger.cs         Persistent negative-result cache
    LrcLibClient.cs              LRCLIB HTTP client, rate limiting, circuit breaker
    LyricsSidecarStore.cs        Writes / removes .lrc and .txt sidecars
    LibraryPathScope.cs          Opt-in library path matching
  Models/                        TrackSignature, LyricsInventory, lookup results
  Configuration/                 Persisted settings
  UI/Config/                     Config page (Emby GenericEdit)
```

## Credits

Lyrics are provided by [LRCLIB](https://lrclib.net). Please respect its usage guidelines; the plugin's rate limiting is designed to do so by default.
