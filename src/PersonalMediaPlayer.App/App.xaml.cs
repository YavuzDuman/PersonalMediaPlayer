using Microsoft.UI.Xaml;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Storage;

namespace PersonalMediaPlayer.App;

public partial class App : Application
{
    private Window? _window;
    private static Mutex? _instanceMutex;
    private static CancellationTokenSource? _handoffListen;

    public App()
    {
        InitializeComponent();
    }

    public static Window MainAppWindow =>
        ((App)Current)._window ?? throw new InvalidOperationException("The main window is not available yet.");

    public static IMediaLibrary MediaLibrary { get; private set; } = null!;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var launch = StreamHandoff.ProtocolArgument(Environment.GetCommandLineArgs());
        if (!StreamHandoff.TryOwn(StreamHandoff.MutexName, out _instanceMutex))
        {
            try
            {
                // No window exists yet, so waiting for the open instance is safe.
                StreamHandoff.SendAsync(StreamHandoff.PipeName, launch ?? "").GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // The running window did not accept the address. This process still leaves.
            }

            Environment.Exit(0);
        }

        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            try
            {
                StreamHandoff.RegisterProtocol(exe);
            }
            catch (Exception)
            {
                // A later launch can register the protocol when the registry is writable.
            }
        }

        var libraryRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PersonalMediaPlayer",
            "Library");
        MediaLibrary = new MediaLibrary(new FileLibraryStore(libraryRoot));

        var window = new MainWindow();
        _window = window;
        ThemeSettings.Apply(window, ThemeSettings.Load());
        _handoffListen = new CancellationTokenSource();
        var token = _handoffListen.Token;
        _ = ListenForHandoffAsync(window, token);
        window.Activate();
        if (launch is not null)
        {
            window.ReceiveHandoff(launch);
        }
    }

    internal static void CancelHandoffListen()
    {
        _handoffListen?.Cancel();
        GC.KeepAlive(_instanceMutex);
    }

    private static async Task ListenForHandoffAsync(MainWindow window, CancellationToken cancellationToken)
    {
        try
        {
            await StreamHandoff.ListenAsync(
                StreamHandoff.PipeName,
                line =>
                {
                    window.DispatcherQueue.TryEnqueue(() => window.ReceiveHandoff(line));
                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (Exception)
        {
            // The window still runs when the handoff pipe cannot be opened.
        }
    }
}
