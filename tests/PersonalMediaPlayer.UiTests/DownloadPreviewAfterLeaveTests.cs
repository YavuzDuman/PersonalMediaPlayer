using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Views;
using Xunit;
using MediaApp = PersonalMediaPlayer.App.App;

namespace PersonalMediaPlayer.UiTests;

public sealed class BlankPage : Page
{
}

public class DownloadPreviewAfterLeaveTests
{
    public void LeavingDuringSaveDoesNotPlayTheNextReadyAudio()
    {
        var folder = Path.Combine(Path.GetTempPath(), "pmp-preview-leave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var queueFile = Path.Combine(folder, "download-queue.json");
        var historyFile = Path.Combine(folder, "download-history.json");
        File.WriteAllText(queueFile, "[]");
        try
        {
            RunScenario(queueFile, historyFile, folder);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void RunScenario(string queueFile, string historyFile, string folder)
    {
        SetStore("PersonalMediaPlayer.App.Download.DownloadQueueStore", queueFile);
        SetStore("PersonalMediaPlayer.App.Download.DownloadHistory", historyFile);

        Exception? scenario = null;
        Application.Start(_ =>
        {
            var queue = DispatcherQueue.GetForCurrentThread();
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(queue));
            var app = new SilentApp();
            queue.TryEnqueue(() =>
            {
                try
                {
                    Exercise(app, queue, folder);
                }
                catch (Exception ex)
                {
                    scenario = ex;
                }
                finally
                {
                    PostQuitMessage(0);
                }
            });
        });
        if (scenario is not null)
        {
            ExceptionDispatchInfo.Capture(scenario).Throw();
        }
    }

    private static void Exercise(MediaApp app, DispatcherQueue dispatcher, string folder)
    {
        var video = Path.Combine(folder, "video.mp4");
        var audio = Path.Combine(folder, "audio.wav");
        File.WriteAllBytes(video, "video"u8.ToArray());
        WriteSilence(audio, milliseconds: 1500);
        Window? window = null;
        DownloadPage? page = null;
        try
        {
            window = new Window();
            typeof(MediaApp).GetField("_window", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(app, window);
            Invoke(Hub(), "Start", dispatcher);
            window.Activate();
            page = new DownloadPage();
            window.Content = page;

            var videoItem = Ready("Lecture", "https://www.youtube.com/watch?v=abcdefghijk", "1080p", "137", audioOnly: false, video);
            var audioItem = Ready("Lecture audio", "https://www.youtube.com/watch?v=abcdefghijl", "Audio only", "140", audioOnly: true, audio);
            Invoke(Hub(), "Add", videoItem);
            Invoke(Hub(), "Add", audioItem);
            Set(page, "_previewItem", videoItem);
            Set(page, "_previewPath", video);
            Set(page, "_listing", new DownloadListing("Lecture", [videoItem.Quality], []));

            var saved = Path.Combine(folder, "saved-video.mp4");
            var leave = typeof(DownloadPage).GetMethod("OnNavigatedFrom", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(leave);
            var host = window;
            var downloadPage = page;
            Func<string, Task<(string Message, string Path)>> write = source =>
            {
                host.Content = new BlankPage();
                leave.Invoke(downloadPage, [null]);
                File.Copy(source, saved, overwrite: true);
                return Task.FromResult(("a folder on this PC", saved));
            };

            var finish = typeof(DownloadPage).GetMethod("FinishSaveAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(finish);
            var pending = (Task)finish.Invoke(page, [write])!;
            if (pending.IsFaulted && pending.Exception is not null)
            {
                ExceptionDispatchInfo.Capture(pending.Exception.InnerException ?? pending.Exception).Throw();
            }

            var opened = Get<MediaPlayer>(page, "_player");
            if (opened is not null)
            {
                Thread.Sleep(700);
            }

            var player = Get<MediaPlayer>(page, "_player");
            var state = player is null ? "no player" : $"{player.State}, playing {player.IsPlaying}";
            Assert.True(player is null, "The download page was no longer visible, but finishing the video save started the ready audio on a hidden player (" + state + ").");
        }
        finally
        {
            if (page is not null)
            {
                try
                {
                    Invoke(page, "ReleasePlayer");
                }
                catch (TargetInvocationException)
                {
                }
            }

            window?.Close();
        }
    }

    private static DownloadQueueItem Ready(string title, string url, string label, string format, bool audioOnly, string path)
    {
        var item = new DownloadQueueItem(title, url, new DownloadQuality(label, format, audioOnly));
        item.MarkReady(path);
        return item;
    }

    private static Type Hub()
        => typeof(DownloadQueueItem).Assembly.GetType("PersonalMediaPlayer.App.Download.DownloadQueueHub")
            ?? throw new InvalidOperationException("DownloadQueueHub was not found.");

    private static void Invoke(object target, string name, params object[] args)
    {
        var type = target as Type ?? target.GetType();
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method.Invoke(target as Type is null ? target : null, args);
    }

    private static void Set(object target, string name, object? value)
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static T? Get<T>(object target, string name) where T : class
        => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target) as T;

    private static void SetStore(string typeName, string path)
    {
        var type = typeof(DownloadQueueItem).Assembly.GetType(typeName) ?? throw new InvalidOperationException(typeName);
        var property = type.GetProperty("StoreOverride", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(property);
        property.SetValue(null, path);
    }

    private static void WriteSilence(string path, int milliseconds)
    {
        const int sampleRate = 8000;
        const short channels = 1;
        const short bits = 16;
        var samples = sampleRate * milliseconds / 1000;
        var dataBytes = samples * channels * bits / 8;
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * bits / 8);
        writer.Write((short)(channels * bits / 8));
        writer.Write(bits);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        writer.Write(new byte[dataBytes]);
    }

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    private sealed class SilentApp : MediaApp
    {
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
        }
    }
}
