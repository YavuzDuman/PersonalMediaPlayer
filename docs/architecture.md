# Architecture

Personal Media Player is a Windows-only C# app. The solution has two projects.

| Project | Role |
| --- | --- |
| `src/PersonalMediaPlayer.App` | WinUI 3 window, pages, capture, recording, playback, and photo tools |
| `src/PersonalMediaPlayer.Core` | Library model and file storage. No XAML |

`PersonalMediaPlayer.App` references Core. Core does not reference WinUI.

```
src/PersonalMediaPlayer.App/
├── Views/            Shell, Library, preview, photo editor, video editor, Capture, Recordings, Settings
├── ViewModels/       Page state. CommunityToolkit.Mvvm
├── Controls/         Media card, folder row, markup surface, video bar
├── Capture/          Screenshots, OCR, recording, system audio
├── Editing/          Photo render, trim, section removal, speed and volume encode, frame grab, timeline thumbnails
├── Playback/         Favorites, bookmarks, resume position
└── Helpers/          Pickers, clipboard, theme, zoom

src/PersonalMediaPlayer.Core/
├── Models/           MediaItem, MediaKind, LibraryFolder
├── Library/          IMediaLibrary, MediaLibrary
└── Storage/          ILibraryStore, FileLibraryStore
```

## UI

`MainWindow` hosts `ShellPage`. The shell is a `NavigationView` with a frame for Library, Recordings, Capture, and Settings.

Leaving a page that holds unsaved work is decided before navigation. Recordings, Capture, the video player, the video editor, and the photo preview expose `PrepareToLeave`. The shell calls it first. If the user must be asked, the page shows its own prompt and navigation waits. A dialog opened from `OnNavigatingFrom` is not used, because that crashes this WinUI desktop app.

The photo stage and the video stage share a black rounded surface. Photo tools sit under the picture. The video bar floats on the video and is the same control for a library video, the video editor, and a recording preview. The editor adds one job at a time for that bar: move the playhead, drag the trim handles, or drag a span to remove. Only one LibVLC video surface is alive. The player clears its surface before the editor creates one, and the editor clears its surface before the player is shown again.

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
| `playback-positions.json` | Where a video was left |
| `screenshot-text.json` | OCR text for library search |
| `theme.txt` | Light, dark, or Windows |
| `include-cursor.txt` | Pointer in screenshots |
| `capture-delay.txt` | 0, 3, 5, or 10 seconds |
| `include-system-audio.txt` | Speaker audio on recordings |

Renaming a library file updates album membership, favorites, bookmarks, and the screenshot text key.

## Capture and playback

Screenshots and recordings use Windows Graphics Capture. A selected recording area captures one monitor and keeps only the dragged rectangle. Speaker audio is WASAPI loopback of the default render device. There is no microphone. Gaps where the speakers send nothing are filled with silence so the track stays aligned with the pictures.

Video playback uses LibVLC (`LibVLCSharp.WinUI` and `VideoLAN.LibVLC.Windows`). The hover frame on the timeline comes from a separate muted `Windows.Media.Playback.MediaPlayer`, so hovering does not seek the LibVLC player. That player is released before a save so it does not keep the file open. A failed save creates the next player only after a new surface exists.

OCR uses `Windows.Media.Ocr`. The image may be scaled up and given more contrast first. Each installed OCR language is tried, and the result with the most words is kept.

Trim, on the player and in the editor, and removed sections use `Windows.Media.Editing`. One `MediaClip` is opened and cloned for each kept span. A fragmented MP4, the kind with `mvex` and `moof` boxes, cannot be opened that way, so the editor first rewrites it as a normal MP4 at the original speed and volume. Speed and volume are then applied by a Media Foundation source reader and sink writer: video is decoded to NV12 and encoded as H.264, and audio is resampled as PCM and encoded as AAC. Both streams are read together. The still-frame button uses a frame-server `MediaPlayer` plus Win2D. Photo pixels use `System.Drawing`.

Bookmarks are stored as times on the source file. An overwrite remaps them: a mark inside a removed span is dropped, earlier removed time is subtracted, and the remaining time is divided by the speed.

## Build

The app project targets `net10.0-windows10.0.26100.0`, with a minimum of Windows 10 version 1809 (`10.0.17763.0`). `WindowsPackageType` is `None`, and the Windows App SDK is self-contained, so the output is an unpackaged exe.

```powershell
dotnet build src/PersonalMediaPlayer.App/PersonalMediaPlayer.App.csproj -c Debug -p:Platform=x64
```

`bin` and `obj` are gitignored. Publish profiles under `Properties/PublishProfiles` are also ignored by the app `.gitignore`.
