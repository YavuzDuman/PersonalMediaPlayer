# Architecture

Personal Media Player is a Windows-only C# app. The solution has the app, the library core, and a small test project.

| Project | Role |
| --- | --- |
| `src/PersonalMediaPlayer.App` | WinUI 3 window, pages, capture, recording, playback, download, and photo tools |
| `tests/PersonalMediaPlayer.Tests` | Tests for download output, stream checks, the browser handoff, playlist listing, captions, saved words, resume positions, HLS rewrite, and language names. It compiles the app sources it covers directly |
| `src/PersonalMediaPlayer.Core` | Library model and file storage. No XAML |
| `extension/` | Unpacked Manifest V3 package for Chrome and Edge. Left out of the solution |

`PersonalMediaPlayer.App` references Core. Core does not reference WinUI.

```
src/PersonalMediaPlayer.App/
├── Views/            Shell, Library, Playlists, Saved words, preview, photo editor, video editor, Download, Merge, Capture, Recordings, Settings
├── ViewModels/       Page state. CommunityToolkit.Mvvm
├── Controls/         Media card, folder row, markup surface, video bar, hover captions, playlist panel, saved-words panel, continue-watching card, crop surface
├── Capture/          Screenshots, OCR, recording, system audio
├── Download/         YouTube lookup, playlist listing, queue, history, subtitle choice
├── Editing/          Photo render, trim, section removal, speed and volume encode, fades, orientation, audio extract, merge, frame grab, timeline thumbnails
├── Playback/         Favorites, bookmarks, resume position, listening volume, playlists, stream check, browser handoff, HLS rewrite, language names
├── Subtitles/        Cue loading, saved words, caption carry through Edit and Merge, the English–Turkish word list, and phrase matching
└── Helpers/          Pickers, clipboard, theme, zoom

extension/
├── manifest.json     Manifest V3. Toolbar action on http and https. Tabs permission only
├── background.js     Sends personalmediaplayer://play/ with the media or page address
├── content.js        Lists direct videos, blob players, and known embed frames
├── picker.html       Choice when a page has several videos
└── picker.js         Picker page script

src/PersonalMediaPlayer.Core/
├── Models/           MediaItem, MediaKind, LibraryFolder
├── Library/          IMediaLibrary, MediaLibrary
└── Storage/          ILibraryStore, FileLibraryStore
```

## UI

`MainWindow` hosts `ShellPage`. The shell is a `NavigationView` with a frame for Library, Playlists, Saved words, Recordings, Download, Merge, Capture, and Settings.

Leaving a page that holds unsaved work is decided before navigation. Recordings, Capture, Download, Merge, the video player, the video editor, and the photo preview expose `PrepareToLeave`. The shell calls it first. If the user must be asked, the page shows its own prompt and navigation waits. A dialog opened from `OnNavigatingFrom` is not used, because that crashes this WinUI desktop app. After the page confirms a leave, it calls `MainWindow.SyncNavigationSelection` so the menu highlight follows the open page. Closing the window runs the same prompt when Merge, the editor, Download, Capture, photo markup, or Recordings still has unsaved work or a save in progress. A video save that is still running blocks the close until it finishes. If Download has nothing waiting, closing pauses the active item and writes the queue. It does not delete the partial file.

The photo stage and the video stage share a black rounded surface. Photo tools sit under the picture. The video bar is the same control for a library video, the video editor, a recording preview, a download preview, and Merge. In the editor it sits under the picture. The editor shows one tool card at a time. Trim is the only mode that shows the white handles. Cut is the only mode that arms remove-part. The editor timeline can zoom from 1× to 16×. Only one LibVLC video surface is alive. The player clears its surface before the editor creates one, and the editor clears its surface before the player is shown again. Download and Merge do the same when their pages close.

## Library storage

`App` creates the library at `%LocalAppData%\PersonalMediaPlayer\Library`. `FileLibraryStore` owns the folders:

```
Library/
├── Images/                 imported and edited photos
├── Videos/                 imported videos, edited videos, and saved recordings
├── Screenshots/            saved captures
├── Recordings/             in-progress takes under .pending, before they are saved
├── Folders/                one JSON file per album, including Recordings
├── Originals/              backups keyed by the library-relative path
└── RecentlyDeleted/        one folder per deleted file, kept for 7 days
```

An item id is the path relative to `Library`, with forward slashes, such as `Images/photo.png`. Albums store those ids. They do not own a separate copy of the file. `Folders/Recordings.json` is created for saved recordings; the video file itself is under `Videos`. Screenshots are the exception: those files stay in `Screenshots`.

Screenshots, Recordings, Unfiled, Photos, Videos, This month, Favorites, Duplicates, and Recently deleted are reserved names. A person cannot create an album with one of those names. Photos, Videos, This month, Favorites, Unfiled, and Duplicates are not JSON files. They are computed when the page loads. Duplicates groups files that share a length and a SHA-256 hash.

Overwrite of an edited photo or an edited video copies the previous bytes to `Originals/{relative path}` before replacing the file. A backup is restored only for that same path. Save as new writes another file under `Videos` and adds it to the same albums as the source, except smart views, Screenshots, and Unfiled.

## Other app data

These files sit next to `Library`, still under `%LocalAppData%\PersonalMediaPlayer\`:

| File | Contents |
| --- | --- |
| `favorites.json` | Paths marked with the heart |
| `playback-bookmarks.json` | Video times and notes |
| `playback-positions.json` | Where a library video or a resolved page was left: time, duration, title, and when it was last watched. A point is kept after 5 seconds and dropped in the last 10 seconds |
| `saved-words.json` | Saved caption words: English, Turkish, sentence, video path or page, whether the page needs a lookup, title, cue time, audio language, caption language |
| `playlists.json` | Named lists of library video paths, and whether each list plays the next video at the end |
| `screenshot-text.json` | OCR text for library search |
| `theme.txt` | Light, dark, or Windows |
| `include-cursor.txt` | Pointer in screenshots |
| `capture-delay.txt` | 0, 3, 5, or 10 seconds |
| `include-system-audio.txt` | Speaker audio on recordings |
| `download-queue.json` | Unfinished downloads: link, quality, progress, and the partial file path. Restored as paused |
| `download-history.json` | Saved downloads: file name, link, quality, date, and location. Removing a row does not delete the file |
| `playback-volume.json` | Listening level and the last audible level. Shared by the player, editor preview, download preview, recordings preview, and Merge |
| `subtitle-cache/` | Extracted captions. A local video uses one `.srt` when no subtitle file sits beside it. A resolved page uses `page-` plus a hash and `.vtt` |
| `tools/` | `yt-dlp.exe`, `ffmpeg.exe`, and `deno.exe` for download, page playback, crop, fade, audio extract, and merge |

Renaming a library file updates album membership, favorites, bookmarks, playlists, saved words, the resume position, and the screenshot text key. YouTube requests use IPv4. The first lookup downloads `yt-dlp` if it is missing. Deno is downloaded only when no JavaScript runtime is already available.

## Capture and playback

Screenshots and recordings use Windows Graphics Capture. A selected recording area captures one monitor and keeps only the dragged rectangle. Speaker audio is WASAPI loopback of the default render device. There is no microphone. Gaps where the speakers send nothing are filled with silence so the track stays aligned with the pictures.

Video playback uses LibVLC (`LibVLCSharp.WinUI` and `VideoLAN.LibVLC.Windows`). LibVLC is started with `--no-sub-autodetect-file`, and each media gets `:no-sub-autodetect-file`, so a `.srt` beside the video is not opened as a second caption track. The hover frame on the timeline comes from a separate muted `Windows.Media.Playback.MediaPlayer`, so hovering does not seek the LibVLC player. That player is released before a save so it does not keep the file open. A failed save creates the next player only after a new surface exists.

Captions in the download preview and the saved-video player are drawn by `HoverCaptions`, above the playback bar. `SubtitleCues` reads a sibling `.srt` whose name starts with the video name, or extracts stream `0:s:0` with `ffmpeg` into `subtitle-cache`. **CC** lives in the playback-settings gear. It turns that text on and off and calls `SetSpu(-1)`, so LibVLC does not paint a second copy. Hovering a word calls `TurkishDictionary`. A phrase of two to four words in the current line is checked first, from `Assets/en-tr-phrases.json`. Otherwise the word is looked up in `Assets/en-tr.json`. Both files ship with the app. Contractions keep their apostrophe. An ending such as `-ing` tries the base with a final `e` before the bare stem, and a stem shorter than three letters is ignored. Nothing is sent off the PC.

Clicking a word opens a card with the English text, the Turkish meaning, and the sentence. **Save** writes `saved-words.json`. The row stores the library path, or a page address with a flag for whether that page must be resolved, plus the cue time and the audio and caption languages selected then. Opening the row seeks when the media can seek and restores those languages. `LanguageLabels` turns codes into English names. `bn` is Bangla, `es-US` is Spanish (United States), and a native-script name is replaced the same way. Original and dubbed audio keep that word. Automatic captions use one English name per language. Uploaded tracks come first, then the spoken automatic track, then other automatic languages. A translated caption is fetched inside the same yt-dlp run with `--sleep-subtitles 65`, because YouTube answers HTTP 429 until that session is old enough. A caption already in `subtitle-cache` opens immediately.

Edit and Merge keep that first caption track. `SubtitleEdit` splits cues across the kept spans, divides them by speed when speed changed, and offsets each merged clip after the one before it. Fade, crop, and rotate leave the cue times alone. The cues are muxed back with ffmpeg as `mov_text`. A failure there leaves the saved picture in place.

OCR uses `Windows.Media.Ocr`. The image may be scaled up and given more contrast first. Each installed OCR language is tried, and the result with the most words is kept.

Trim on the player, and a trim or cut in the editor that does not also change speed, volume, or crop, uses `Windows.Media.Editing`. One `MediaClip` is opened and cloned for each kept span. A fragmented MP4, the kind with `mvex` and `moof` boxes, cannot be opened that way, so that path first rewrites it as a normal MP4 at the original speed and volume. Trim, cut, and crop together, with no speed or volume change, go through `MediaTranscoder` with hardware encoding. A crop that transcoder cannot apply is written by `ffmpeg` instead. Speed, volume, and a crop whose picture size is still unknown use the Media Foundation source reader and sink writer: video is decoded to NV12 and encoded as H.264, and audio is resampled as PCM and encoded as AAC. Both streams are read together. The sink writer is created with throttling disabled, so a save is not paced to the length of the video. Hardware transforms are tried first and the software encoder is the fallback. The still-frame button uses a frame-server `MediaPlayer` plus Win2D. Photo pixels use `System.Drawing`.

Editor fades are applied after the other edits, on the finished timeline. A video fade with no audio fade copies the audio. `ffmpeg` is also used for rotation, flip, audio extract, and fades. Extract writes MP3 or WAV and does not open the source as a `MediaClip`.

Merge reads each file with `ffmpeg`, then joins the kept spans in `VideoMerger`. Every clip is trimmed, faded, scaled into one even frame no wider than 1920 pixels, and set to 30 fps. Audio is 48 kHz stereo. A clip with no audio gets silence for the kept length so the next clip stays in time. Hardware H.264 encoders are tried first, then `libx264`. The preview clock is the player time minus that clip’s trim start, added to the kept lengths before it. A seek holds the slider until playback is near that time.

Download shells out to `yt-dlp` over IPv4. `TryNormalize` accepts `youtube.com`, `m.youtube.com`, `music.youtube.com`, and `youtu.be`. A single video uses `--no-playlist`. Lookup reads uploaded tracks from `subtitles`, then automatic captions, one English name per language, including translations. The chosen language is stored on the queue item. A translated track adds `--sleep-subtitles 65` to that download. A video download asks `yt-dlp` for `--write-subs` or `--write-auto-subs`, then `--embed-subs` and `--convert-subs srt`. Audio-only items skip that. A playlist address, or a `list=` link with no video id, is read with `--yes-playlist --flat-playlist --playlist-end 200` and no subtitle request. The list stops at 200 episodes. The user picks episodes and one shared selector: best, 1080, 720, 480, 360, or audio only. Each chosen episode is queued as its own watch URL. A watch link that also carries `list=` stays one video. A queue item remembers its output path, so pause and a later resume continue the partial file. History thumbnails are YouTube image URLs derived from the saved link. **Open** builds a `MediaItem` for that path and navigates to the video player. It does not launch another app.

Bookmarks are stored as times on the source file. An overwrite remaps them: a mark inside a removed span is dropped, earlier removed time is subtracted, and the remaining time is divided by the speed.

## Streaming

`StreamLink` accepts an absolute `http` or `https` URL. The scheme check finishes on the caller. Header reads, redirects, and a short ranged read run off the UI thread, and only while every hop stays `http` or `https`. The player opens when the final response is `video/*`, an HLS playlist type, or a generic type whose first bytes are a known container. An HTML page shows an error and leaves the current page in place. Library **Open link** uses that check. A missing file ending is allowed.

The extension sends `personalmediaplayer://play/?url=` plus the encoded address. A direct file can include the page as a referrer and the element’s current time. A blob player or a known embed sends the page or the embed address with `resolve=1`. Each primary launch registers the `personalmediaplayer` scheme for the current user, pointed at the running exe. A second process writes one line to the named pipe `PersonalMediaPlayer.StreamHandoff` and exits. The open window brings itself forward. Unsaved work on Merge, the editor, Download, Capture, photo markup, Recordings, or an open trim runs `PrepareToLeave` before the player opens.

`resolve=1` calls `YoutubeDownloader.ResolvePlaybackAsync` (`--write-auto-subs --dump-single-json`, IPv4, Deno when present) and does not call `DownloadAsync`. A combined picture-and-sound stream is preferred when it is at least as tall as a silent picture. Otherwise LibVLC plays the picture and `Media.AddSlave` attaches the audio. An `m3u8` address is the media. When any non-fragmented video exists, fragmented DASH formats are dropped. A page that only has DASH still uses the picture plus an audio slave. LibVLC 3.0.21 rejects an HLS master with six or more audio renditions in one group, so `HlsMaster` rewrites that master to the selected language, keeps at most five renditions in a group, strips subtitle groups, and LibVLC opens the local copy. The rewritten playlist keeps the original language code and `NAME`. Playback settings offer quality, audio, and captions for that stream.

Resume position and bookmarks for a resolved page use the page address. **Save a copy** enqueues that page on the Download queue and leaves the stream playing. The row stays queued until Download is opened. Replay, and one mid-play failure, look the page up again and seek to the last position only when the new stream can seek. A DRM format, or a yt-dlp error that mentions DRM, shows “This video is protected by a license and stays in the browser.” Other lookup failures show the yt-dlp text. The app does not read browser cookie stores.

A stream hides Edit, trim, restore, the snapshot button, timeline hover frames, and the playlist panel. Saved words stay available. Seek, the 10-second skips, and section repeat turn on only after LibVLC says the media can seek and a length is known. A direct stream stores a saved word against the media address and does not record resume or bookmarks. `dotnet build` does not build `extension/`.

## Build

The app project targets `net10.0-windows10.0.26100.0`, with a minimum of Windows 10 version 1809 (`10.0.17763.0`). `WindowsPackageType` is `None`, and the Windows App SDK is self-contained, so the output is an unpackaged exe.

```powershell
dotnet build src/PersonalMediaPlayer.App/PersonalMediaPlayer.App.csproj -c Debug -p:Platform=x64
```

`bin` and `obj` are gitignored. Publish profiles under `Properties/PublishProfiles` are also ignored by the app `.gitignore`. The extension stays a loose folder, so this build does not compile it.
