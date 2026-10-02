# Project structure

The current layout and storage model are described in [docs/architecture.md](docs/architecture.md).

How to use the app is in [docs/guide.md](docs/guide.md).

Tests live in `tests/PersonalMediaPlayer.Tests`. They compile the app sources they cover and check download output, stream checks, the browser handoff, playlist listing, captions, saved words, and resume positions.

The shell opens on Home. The other pages are Library, Playlists, Saved words, Recordings, Download, Merge, Capture, and Settings. Home headings use All, beside the title, to open the full list. A playlist on Home is a cover of up to four videos. Play on the cover starts the first one that can be opened, and the card opens that playlist. Hovering previews the videos. On the Playlists page, search filters by name, and drag or sort saves the order. The player panel shows a picture on each row and can reorder the list while a video plays. **Words** opens the saved-words list beside the video and hides it completely. Import can copy a file into the library or link it in place through `Library/links.json`. Library can select several files and add them to a playlist or to favorites together. The browser extension is the unpacked package in `extension/` and is left out of `PersonalMediaPlayer.slnx`.

Merge, the video editor, the shared playback bar, stream playback, and the subtitle hover list live under `src/PersonalMediaPlayer.App`.
