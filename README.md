<div align="center">

# Personal Media Player

A Windows desktop library for photos, videos, screenshots, and screen recordings.  
Files stay on this PC. Albums are JSON lists. There is no database.

[![Windows](https://img.shields.io/badge/Windows-10_1809_%7C_11-0078D4?logo=windows&logoColor=white)](#requirements)
[![.NET](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#requirements)
[![WinUI](https://img.shields.io/badge/WinUI-3-blue)](#requirements)
[![Guide](https://img.shields.io/badge/docs-user_guide-2ea44f)](docs/guide.md)
[![Architecture](https://img.shields.io/badge/docs-architecture-6e7781)](docs/architecture.md)

</div>

---

## Start here

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then from this folder:

```powershell
dotnet run --project src/PersonalMediaPlayer.App/PersonalMediaPlayer.App.csproj -c Debug -p:Platform=x64
```

Visual Studio 2022 can open `PersonalMediaPlayer.slnx` and start **PersonalMediaPlayer.App**.

The first launch creates the library at `%LocalAppData%\PersonalMediaPlayer\Library`. Settings has a button that opens that folder.

## The window

```mermaid
flowchart LR
  subgraph shell [Shell]
    direction TB
    Library
    Playlists
    Words[Saved words]
    Recordings
    Download
    Merge
    Capture
    Settings
  end

  Library --> Views[Albums and smart views]
  Library --> Link[Open a direct link]
  Views --> Photo[Photo preview]
  Views --> Player[Video player]
  Link --> Player
  Playlists --> Player
  Words --> Player
  Player --> Editor[Video editor]
  Capture --> Shot[Screenshot preview]
  Recordings --> Take[Recording preview]
  Download --> Queue[Queue and history]
  Merge --> Joined[One saved video]
  Photo --> Folder[(Library folder)]
  Player --> Folder
  Editor --> Folder
  Shot --> Folder
  Take --> Folder
  Queue --> Folder
  Joined --> Folder
```

| Area | What you do there |
| --- | --- |
| **Library** | Import, search, album, favorite, slideshow, continue a video you left, open a direct link, and find duplicate files |
| **Playlists** | Collect library videos and play them in order |
| **Saved words** | Open a caption word you saved, at that moment, with the audio and captions from then |
| **Photos** | Draw, blur, crop, rotate, compare with the original, and read a selected area |
| **Videos** | Play a file or a stream, hover the timeline for a frame, bookmark, resume, trim, edit, grab a still, and save caption words with a Turkish meaning |
| **Merge** | Order clips, trim and fade each one, preview the sequence, and save one video. The first caption track comes along |
| **Capture** | Shoot an area, window, monitor, every display, or turn a text file into an image |
| **Recordings** | Record a monitor, a window, or a rectangle, with optional speaker audio |
| **Download** | Queue a YouTube video or the playlist episodes you check, choose an uploaded subtitle or an automatic caption, preview, then save into the library or any PC folder |
| **Settings** | Theme, library folder, screenshot delay, cursor, and the shortcut list |

Details for each screen are in the [user guide](docs/guide.md).

## Library at a glance

Import or drop files. Photos: `png` `jpg` `jpeg` `webp` `bmp` `gif`. Videos: `mp4` `mkv` `mov` `avi` `wmv` `webm` `m4v` `m4a`.

| View | Contents |
| --- | --- |
| All media | Every file the app owns |
| Photos / Videos | Split by type, including screenshots and recordings |
| This month | Imported during the current calendar month |
| Favorites | Heart on the thumbnail. The heart shows while the pointer is over the card |
| Unfiled | Not in an album you created |
| Duplicates | Same bytes, even when the names differ |
| Screenshots / Recordings | Saved captures and saved takes |
| Your albums | Membership only. The file stays in one place on disk |
| Recently deleted | Restorable for 7 days |

**Add to** keeps the current album. **Move to** leaves it. Deleting from a smart view or from All media removes the file. Deleting from an album you created only drops that membership. Deleting a favorite only clears the heart.

Search covers the name, the type, the album, and words read from a screenshot. A text hit opens with the matching words boxed on the preview. The saved image is unchanged.

## Picture, player, capture

<div align="center">

| Photo | Video | Capture and recording |
| --- | --- | --- |
| Pen, arrow, rectangle, text, blur, crop | Shared control bar on the picture | Area, window, monitor, all displays, fullscreen |
| Read the whole shot, or only the box you drag | Hover shows a frame and a time. Click or drag seeks | Text file drawn as one tall image |
| Slider compares the original and the saved file | Bookmarks, resume, replay, captions with hover meanings, playback settings for quality, audio, and a repeated section, and an editor for speed, sound, rotate, fade, crop, trim, and cuts | Speaker audio on a recording. No microphone |
| Restore puts the backup back | Still frame of the current moment | Shortcuts work while the app is in the background |

</div>

Leaving a screenshot, a marked photo, a trimmed video, an open video edit, an unsaved recording, an unfinished download, or an unsaved merge asks you to stay or leave before the page changes. Closing the window with X asks the same question when that work is still open.

**Open link** on Library plays a direct video address and leaves the library unchanged. The unpacked extension in `extension/` can send a page video, or a page such as YouTube, into that same player. **Save a copy** queues the page on Download and leaves the stream playing. A video that needs a license stays in the browser. Load the extension unpacked in Chrome or Edge. The app build does not include it.

## Screenshot shortcuts

These do not replace Print Screen.

| Shortcut | Captures |
| --- | --- |
| `Ctrl` `Alt` `Shift` `A` | Selected area |
| `Ctrl` `Alt` `Shift` `W` | Single window |
| `Ctrl` `Alt` `Shift` `M` | Single monitor |
| `Ctrl` `Alt` `Shift` `D` | All monitors |
| `Ctrl` `Alt` `Shift` `F` | Display under the pointer |

Delay is 0, 3, 5, or 10 seconds. OCR uses the Windows language packs you have installed.

## Requirements

| Piece | Used for |
| --- | --- |
| Windows 10 version 1809 or later, or Windows 11 | The app itself |
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Build and `dotnet run` |
| A display | Graphics Capture for screenshots and recording |
| Windows OCR language pack | Screenshot search and reading text. Optional |

The build is an unpackaged WinUI 3 executable. Developer Mode is not required.

## Read next

| Document | What it covers |
| --- | --- |
| [docs/guide.md](docs/guide.md) | How each page behaves |
| [docs/architecture.md](docs/architecture.md) | Projects, folders on disk, and the Windows APIs behind the features |

```powershell
dotnet build src/PersonalMediaPlayer.App/PersonalMediaPlayer.App.csproj -c Debug -p:Platform=x64
```
