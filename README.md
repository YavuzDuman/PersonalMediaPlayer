# Personal Media Player

Windows desktop app for a personal media library. This first slice is a WinUI 3 shell: import images into the app and browse them.

## Run

The app is unpackaged so it runs as a normal desktop executable (no Developer Mode required).

```powershell
dotnet run --project src/PersonalMediaPlayer.App/PersonalMediaPlayer.App.csproj -p:Platform=x64
```

Or open `PersonalMediaPlayer.slnx` in Visual Studio 2022 and start **PersonalMediaPlayer.App (Unpackaged)**.

## First slice

- Import images (PNG, JPEG, WebP, BMP, GIF)
- Browse thumbnails in Library
- Open a preview
- Files are copied into the app library folder (see Settings)

Recordings, capture, playback, and editing are placeholders for later.

## Layout

See [PROJECT_STRUCTURE.md](PROJECT_STRUCTURE.md).
