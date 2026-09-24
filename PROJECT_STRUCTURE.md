# Personal Media Player — Project Structure

This document describes the proposed layout for the Windows desktop app **before any project is scaffolded**. Review this first. Implementation starts only after you are happy with the shape.

**Stack:** C# / .NET 10 / WinUI 3 / Windows App SDK (unpackaged desktop exe)  
**OS:** Windows 10/11 only  
**First slice:** modern desktop shell + import images into the app library + browse them

Capture, screen recording, video playback, trim, zoom, and speed come later. Folders and navigation entries for those are reserved so we do not reshuffle the tree later.

---

## What the first slice is

A desktop window that already feels like the product:

- Left navigation (Library now; Capture, Recordings, Settings later)
- A gallery of media the app owns
- An **Import** action: pick image files from disk, copy them into the app library, then show them
- Click an item to preview it

“Export images to our app” is treated as **import into the library**. The app copies files into its own folder so browsing does not depend on the original path still existing.

Images only in this slice. Video/recording files use the same library model later.

---

## Solution layout

Two projects is enough. UI stays in the app. Library logic stays in Core so capture and playback can reuse it without touching XAML.

```
PersonalMediaPlayer/
├── PROJECT_STRUCTURE.md          ← this file
├── README.md                     ← short run instructions (added when we scaffold)
├── PersonalMediaPlayer.sln
└── src/
    ├── PersonalMediaPlayer.App/          # WinUI 3 desktop UI
    └── PersonalMediaPlayer.Core/         # models, library, disk storage (no UI)
```

Later, without moving existing code:

```
src/
    PersonalMediaPlayer.Capture/          # screenshots, screen recording
    PersonalMediaPlayer.Playback/         # video player, speed, zoom
    PersonalMediaPlayer.Editing/          # trim, export
```

Those three are **not** created now.

---

## App project (`PersonalMediaPlayer.App`)

WinUI 3 unpackaged desktop app. MVVM. Views render; ViewModels talk to Core.

```
src/PersonalMediaPlayer.App/
├── App.xaml
├── App.xaml.cs
├── MainWindow.xaml                       # window chrome, Mica, hosts the shell
├── MainWindow.xaml.cs
├── Package.appxmanifest
├── app.manifest
├── Assets/                               # app icon, placeholder artwork
│   └── ...
├── Views/
│   ├── ShellPage.xaml                    # NavigationView + content frame
│   ├── ShellPage.xaml.cs
│   ├── LibraryPage.xaml                  # image gallery (first real page)
│   ├── LibraryPage.xaml.cs
│   └── MediaPreviewPage.xaml             # larger preview of one item
│       └── MediaPreviewPage.xaml.cs
├── ViewModels/
│   ├── ShellViewModel.cs
│   ├── LibraryViewModel.cs
│   └── MediaPreviewViewModel.cs
├── Controls/
│   └── MediaCard.xaml                    # thumbnail + title + date
│       └── MediaCard.xaml.cs
├── Converters/
│   └── ...                               # path → BitmapImage, etc.
└── Helpers/
    ├── NavigationHelper.cs
    └── FilePickerHelper.cs               # HWND-aware WinUI file picker
```

No `Services/` folder in the App project. Import, listing, and storage live in Core. The App only adapts WinUI pickers and navigation to those services.

---

## Core project (`PersonalMediaPlayer.Core`)

Class library. No WinUI, no XAML. Unit-testable later.

```
src/PersonalMediaPlayer.Core/
├── Models/
│   ├── MediaItem.cs                      # id, kind, display name, path, created
│   └── MediaKind.cs                      # Image now; Video / Recording later
├── Library/
│   ├── IMediaLibrary.cs                  # import, list, get by id, delete (later)
│   └── MediaLibrary.cs
└── Storage/
    ├── ILibraryStore.cs                  # copy file in, enumerate files
    └── FileLibraryStore.cs               # %LocalAppData%\PersonalMediaPlayer\Library
```

### Domain model (first slice)

```csharp
enum MediaKind { Image, Video, Recording }   // only Image is used now

class MediaItem
{
    string Id;             // relative path inside the library, e.g. Images/photo.png
    MediaKind Kind;
    string DisplayName;    // original file name
    string FilePath;       // inside the app library folder
    DateTimeOffset ImportedAt;
}
```

### Library location

```
{ApplicationData.LocalFolder}\Library\
└── Images\
    └── original-name.png | .jpg | .jpeg | .webp | .bmp | .gif
```

Import copies the file and keeps the original file name. Collisions become `name (1).png`. `DisplayName` is the file name. No database — the folder **is** the library.

---

## UI shell

```
┌─────────────────────────────────────────────────────────────┐
│  Personal Media Player                                  _ □ x│
├──────────────┬──────────────────────────────────────────────┤
│              │  Library                          [ Import ] │
│  Library     │                                              │
│  Recordings  │  ┌────────┐  ┌────────┐  ┌────────┐         │
│  Capture     │  │ thumb  │  │ thumb  │  │ thumb  │         │
│  Settings    │  │ name   │  │ name   │  │ name   │         │
│              │  └────────┘  └────────┘  └────────┘         │
│              │  empty state: “Import images to get started” │
└──────────────┴──────────────────────────────────────────────┘
```

| Nav item     | First slice                         | Later                         |
|--------------|-------------------------------------|-------------------------------|
| Library      | Image gallery + Import              | Images + videos together      |
| Recordings   | Placeholder page                    | Screen recordings list        |
| Capture      | Placeholder page                    | Screenshot / record           |
| Settings     | Placeholder (library path, about)   | Encoder, audio, theme         |

**Library page**

- Toolbar: **Import** (multi-select images)
- Adaptive thumbnail grid
- Empty state when the library is empty
- Click a card → preview page (or a side pane — preview page is simpler and cleaner)

**Import flow**

1. User clicks Import.
2. WinUI file picker, images only, multi-select.
3. Core copies each file into `Library\Images\`.
4. Library page refreshes from disk.

---

## How the pieces talk

```
LibraryPage
    └─ LibraryViewModel
           ├─ FilePickerHelper          (WinUI, App project)
           └─ IMediaLibrary             (Core)
                  └─ ILibraryStore      (copy + enumerate files)
```

- ViewModel never touches disk paths directly.
- Core never references `Microsoft.UI.Xaml`.
- Capture later calls the same `IMediaLibrary.Import(...)` with a screenshot or recording file.

---

## Dependencies (first slice)

| Package / SDK              | Where        | Why                                      |
|----------------------------|--------------|------------------------------------------|
| Windows App SDK / WinUI 3  | App          | Desktop UI                               |
| CommunityToolkit.Mvvm      | App          | `ObservableObject`, commands             |
| CommunityToolkit.WinUI     | App          | Layout helpers if needed                 |

No FFmpeg, no SQLite, no capture APIs, no MediaPlayer in this slice.

---

## What we are deliberately not building yet

- Screenshots and screen recording
- Video playback, trim, zoom, speed
- Drag-and-drop (easy follow-up)
- Editing originals in place (we copy into the library)
- Cloud sync, plugins, accounts

Placeholder nav pages exist so the shell looks complete; they can show a short “Coming next” message.

---

## Naming

| Term in the UI | Meaning |
|----------------|---------|
| Library        | Media the app owns |
| Import         | Add files from disk into the library |
| Preview        | View one item larger |

---

## After you approve this file

1. Scaffold the solution and the two projects as drawn above.
2. Build the shell (Mica window + navigation).
3. Implement Import + image gallery + preview.

Say if you want anything renamed, merged, or dropped before that.
