using Microsoft.UI.Dispatching;
using PersonalMediaPlayer.Core.Library;

namespace PersonalMediaPlayer.App.Storage;

internal static class LibraryWatch
{
    public static event EventHandler? Changed;

    public static void Start(IMediaLibrary library, DispatcherQueue queue)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var scan = library.RefreshConnectedFolders();
                if (scan.Added > 0)
                {
                    queue.TryEnqueue(() => Changed?.Invoke(null, EventArgs.Empty));
                }
            }
            catch (Exception)
            {
                // The library still opens when a connected folder cannot be read.
            }
        });
    }
}
