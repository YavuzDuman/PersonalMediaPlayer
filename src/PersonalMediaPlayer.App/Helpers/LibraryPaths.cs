using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Helpers;

internal static class LibraryPaths
{
    public static void Move(string oldPath, string newPath)
    {
        PlaybackBookmarks.Move(oldPath, newPath);
        PlaybackProgress.Move(oldPath, newPath);
        SavedWords.Move(oldPath, newPath);
        Playlists.MoveFile(oldPath, newPath);
        MediaFavorites.Move(oldPath, newPath);
        ScreenshotTextIndex.Move(oldPath, newPath);
        DownloadHistory.Move(oldPath, newPath);
        DownloadPoster.Move(oldPath, newPath);
    }
}
