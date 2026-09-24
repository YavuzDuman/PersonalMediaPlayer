using Microsoft.UI.Xaml;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Storage;

namespace PersonalMediaPlayer.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    public static Window MainAppWindow =>
        ((App)Current)._window ?? throw new InvalidOperationException("The main window is not available yet.");

    public static IMediaLibrary MediaLibrary { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var libraryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "Library");
        MediaLibrary = new MediaLibrary(new FileLibraryStore(libraryRoot));

        _window = new MainWindow();
        ThemeSettings.Apply(_window, ThemeSettings.Load());
        _window.Activate();
    }
}
