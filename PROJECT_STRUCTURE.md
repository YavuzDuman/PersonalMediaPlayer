# Project structure

The current layout and storage model are described in [docs/architecture.md](docs/architecture.md).

How to use the app is in [docs/guide.md](docs/guide.md).

Tests live in `tests/PersonalMediaPlayer.Tests`. They compile the app sources they cover and check download output, stream checks, the browser handoff, playlist listing, captions, saved words, and resume positions.

The shell pages are Library, Playlists, Saved words, Recordings, Download, Merge, Capture, and Settings. The browser extension is the unpacked package in `extension/` and is left out of `PersonalMediaPlayer.slnx`.

Merge, the video editor, the shared playback bar, stream playback, and the subtitle hover list live under `src/PersonalMediaPlayer.App`.
