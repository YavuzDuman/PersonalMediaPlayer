using System.Diagnostics;
using System.Drawing.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.ViewModels;
using Windows.Storage;
using WinRT.Interop;
using Bitmap = System.Drawing.Bitmap;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class CapturePage : Page
{
    private bool _allowLeave;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;
    private int _textReadGeneration;

    public CapturePage()
    {
        ViewModel = new CaptureViewModel(App.MediaLibrary);
        InitializeComponent();
        Markup.MarksChanged += (_, _) =>
        {
            ViewModel.RecognizedText = string.Empty;
            _ = ReadCurrentTextAsync();
        };
        Markup.ImageChanged += async (_, args) =>
        {
            await ViewModel.ReplaceImageAsync(args.PngBytes, args.Width, args.Height);
        };
    }

    public CaptureViewModel ViewModel { get; }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _allowLeave = false;
        if (e.Parameter is PendingScreenshot shot)
        {
            _ = ShowGrabbedFrameAsync(shot);
            return;
        }

        if (e.Parameter is CaptureKind kind)
        {
            RequestCapture(kind);
        }
    }

    private async Task ShowGrabbedFrameAsync(PendingScreenshot shot)
    {
        await ViewModel.ShowPendingAsync(shot.Png, shot.Width, shot.Height, shot.Name);
        Markup.CanEdit = true;
        Markup.Load(ViewModel.PreviewImage, ViewModel.PixelWidth, ViewModel.PixelHeight, ViewModel.PngBytes);
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (!ViewModel.HasUnsaved)
        {
            return true;
        }

        ShowLeavePrompt();
        return false;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || !ViewModel.HasUnsaved)
        {
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == NavigationMode.Back;
        if (LeavePrompt.Visibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(ShowLeavePrompt);
        }
    }

    private void ShowLeavePrompt()
    {
        LeavePromptText.Text = "This shot is not in Screenshots. Leave without saving?";
        LeavePrompt.Visibility = Visibility.Visible;
    }

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
    }

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        ViewModel.Clear();
        Markup.Clear();
        _allowLeave = true;
        ContinueNavigation();
    }

    private void ContinueNavigation()
    {
        var moved = false;
        if (_pendingIsBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            moved = true;
        }
        else if (_pendingPageType is not null && Frame.Navigate(_pendingPageType, _pendingParameter))
        {
            Frame.BackStack.Clear();
            moved = true;
        }

        if (moved && App.MainAppWindow is MainWindow window)
        {
            window.SyncNavigationSelection();
        }
    }

    internal void RequestCapture(CaptureKind kind)
    {
        ViewModel.CaptureModeIndex = (int)kind;
        ScreenshotSession.ShowWindow(App.MainAppWindow);
        App.MainAppWindow.Activate();
        _ = StartCaptureAsync();
    }

    private void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        => _ = StartCaptureAsync();

    private async Task StartCaptureAsync()
    {
        if (ViewModel.IsBusy || ScreenshotSession.Current is not null)
        {
            return;
        }

        if (ViewModel.HasUnsaved)
        {
            var discard = new ContentDialog
            {
                Title = "Discard unsaved screenshot?",
                Content = "This shot is not in Screenshots. Taking a new one throws it away.",
                PrimaryButtonText = "Discard and capture",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await discard.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            ViewModel.Clear();
            Markup.Clear();
        }

        if (ViewModel.Kind == CaptureKind.TextFile)
        {
            await CaptureTextFileAsync();
            return;
        }

        ViewModel.IsBusy = true;
        var window = App.MainAppWindow;
        var restored = false;
        void Restore()
        {
            if (restored)
            {
                return;
            }

            restored = true;
            ScreenshotSession.ShowWindow(window);
        }

        using var abort = new CancellationTokenSource();
        ScreenshotSession.BeginPrepare(abort);
        try
        {
            var delay = ViewModel.DelaySeconds;
            for (var remaining = delay; remaining > 0; remaining--)
            {
                ViewModel.ShowStatus($"Capturing in {remaining}…", InfoBarSeverity.Informational);
                await Task.Delay(1000, abort.Token);
            }

            ViewModel.ShowStatus("Capturing screens…", InfoBarSeverity.Informational);
            ScreenshotSession.HideWindow(window);
            await Task.Delay(250, abort.Token);

            switch (ViewModel.Kind)
            {
                case CaptureKind.AllMonitors:
                    await CaptureDirectAsync(
                        () => GraphicsCaptureService.CaptureAllMonitorsAsync(abort.Token, ViewModel.IncludeCursor),
                        Restore);
                    break;
                case CaptureKind.Fullscreen:
                    await CaptureDirectAsync(
                        () => GraphicsCaptureService.CaptureMonitorAsync(
                            ScreenGeometry.FromCursor(),
                            abort.Token,
                            ViewModel.IncludeCursor),
                        Restore);
                    break;
                case CaptureKind.SingleMonitor:
                    await StartFrozenOverlayAsync(window, CaptureOverlayKind.Monitor, Restore, abort.Token);
                    break;
                case CaptureKind.SingleWindow:
                    StartWindowPick(window, Restore);
                    break;
                default:
                    await StartFrozenOverlayAsync(window, CaptureOverlayKind.Region, Restore, abort.Token);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            Restore();
            ViewModel.ShowStatus("Screenshot cancelled.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            Restore();
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ScreenshotSession.EndPrepare();
            ViewModel.IsBusy = false;
        }
    }

    private async Task CaptureDirectAsync(Func<Task<Bitmap>> capture, Action restore)
    {
        Bitmap bitmap;
        try
        {
            bitmap = await capture();
        }
        catch (OperationCanceledException)
        {
            restore();
            ViewModel.ShowStatus("Screenshot cancelled. Nothing was saved.", InfoBarSeverity.Informational);
            return;
        }
        catch (Exception ex)
        {
            restore();
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        await PreviewScreenshotAsync(bitmap);
    }

    private async Task StartFrozenOverlayAsync(
        Window window,
        CaptureOverlayKind overlayKind,
        Action restore,
        CancellationToken cancellationToken)
    {
        Bitmap desktop;
        try
        {
            desktop = await GraphicsCaptureService.CaptureAllMonitorsAsync(cancellationToken, ViewModel.IncludeCursor);
        }
        catch (OperationCanceledException)
        {
            restore();
            ViewModel.ShowStatus("Screenshot cancelled. Nothing was saved.", InfoBarSeverity.Informational);
            return;
        }
        catch (Exception ex)
        {
            restore();
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
            return;
        }

        ViewModel.ShowStatus(
            overlayKind == CaptureOverlayKind.Monitor
                ? "Click a monitor to capture it. Esc or Alt+F4 cancels."
                : "Select an area on any screen. Esc or Alt+F4 cancels.",
            InfoBarSeverity.Informational);
        var session = new ScreenshotSession(
            window,
            desktop,
            ScreenGeometry.VirtualDesktop,
            ScreenGeometry.Monitors,
            overlayKind,
            bitmap => DispatcherQueue.TryEnqueue(() => _ = PreviewScreenshotAsync(bitmap)),
            OnOverlayCancelled);
        session.ShowOverlays();
    }

    private void StartWindowPick(Window window, Action restore)
    {
        ScreenGeometry.Refresh();
        ViewModel.ShowStatus("Click a window to capture it. Esc or Alt+F4 cancels.", InfoBarSeverity.Informational);
        var session = new ScreenshotSession(
            window,
            desktop: null,
            ScreenGeometry.VirtualDesktop,
            ScreenGeometry.Monitors,
            CaptureOverlayKind.Window,
            onComplete: null,
            OnOverlayCancelled,
            point => DispatcherQueue.TryEnqueue(() => _ = CapturePickedWindowAsync(point)));
        try
        {
            session.ShowOverlays();
        }
        catch
        {
            restore();
            throw;
        }
    }

    private async Task CapturePickedWindowAsync(System.Drawing.Point point)
    {
        var host = App.MainAppWindow;
        try
        {
            await Task.Delay(80);
            var hwnd = WindowHitTest.RootFromPoint(point.X, point.Y, WindowNative.GetWindowHandle(host));
            if (hwnd == 0)
            {
                throw new InvalidOperationException("Click a visible window to capture.");
            }

            var bitmap = await GraphicsCaptureService.CaptureWindowAsync(hwnd, includeCursor: ViewModel.IncludeCursor);
            await PreviewScreenshotAsync(bitmap);
        }
        catch (Exception ex)
        {
            ScreenshotSession.ShowWindow(host);
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void OnOverlayCancelled()
    {
        if (!ViewModel.HasPreview)
        {
            ViewModel.ShowStatus("Screenshot cancelled. Nothing was saved.", InfoBarSeverity.Informational);
            return;
        }

        ViewModel.ShowStatus("Screenshot cancelled.", InfoBarSeverity.Informational);
    }

    private async Task CaptureTextFileAsync()
    {
        var file = await FilePickerHelper.PickTextFileAsync(App.MainAppWindow);
        if (file is null)
        {
            return;
        }

        ViewModel.IsBusy = true;
        try
        {
            ViewModel.ShowStatus("Rendering text file…", InfoBarSeverity.Informational);
            var path = file.Path;
            var bitmap = await Task.Run(() => TextFileRenderer.Render(path));
            var name = Path.GetFileNameWithoutExtension(file.Name);
            await PreviewScreenshotAsync(bitmap, name);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            ViewModel.IsBusy = false;
        }
    }

    private async Task PreviewScreenshotAsync(Bitmap bitmap, string? suggestedName = null)
    {
        try
        {
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            await ViewModel.ShowPendingAsync(stream.ToArray(), bitmap.Width, bitmap.Height, suggestedName);
            Markup.CanEdit = true;
            Markup.Load(ViewModel.PreviewImage, ViewModel.PixelWidth, ViewModel.PixelHeight, ViewModel.PngBytes);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            bitmap.Dispose();
            ScreenshotSession.ShowWindow(App.MainAppWindow);
        }
    }

    private void ReadText_Click(object sender, RoutedEventArgs e)
        => _ = ReadCurrentTextAsync();

    private async Task ReadCurrentTextAsync()
    {
        if (ViewModel.PngBytes is not { Length: > 0 } bytes)
        {
            return;
        }

        var generation = ++_textReadGeneration;
        ViewModel.IsReadingText = true;
        ViewModel.RecognizedText = string.Empty;
        try
        {
            ViewModel.ShowStatus("Reading text…", InfoBarSeverity.Informational);
            var text = await ScreenshotOcr.ReadAsync(Markup.Flatten(bytes));
            if (generation != _textReadGeneration)
            {
                return;
            }

            ViewModel.RecognizedText = text;
            ViewModel.ShowStatus(
                string.IsNullOrWhiteSpace(text) ? "No text found in this screenshot." : "Text read from the screenshot.",
                string.IsNullOrWhiteSpace(text) ? InfoBarSeverity.Informational : InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            if (generation != _textReadGeneration)
            {
                return;
            }

            ViewModel.RecognizedText = string.Empty;
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (generation == _textReadGeneration)
            {
                ViewModel.IsReadingText = false;
            }
        }
    }

    private void CopyText_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ViewModel.RecognizedText))
        {
            return;
        }

        try
        {
            ClipboardHelper.CopyText(ViewModel.RecognizedText);
            ViewModel.ShowStatus("Text copied to clipboard.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PngBytes is not { Length: > 0 })
        {
            return;
        }

        try
        {
            await ClipboardHelper.CopyPngAsync(Markup.Flatten(ViewModel.PngBytes));
            ViewModel.ShowStatus("Copied to clipboard.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.PngBytes is not { Length: > 0 })
        {
            return;
        }

        try
        {
            var png = Markup.Flatten(ViewModel.PngBytes);
            using var stream = new MemoryStream(png);
            var item = App.MediaLibrary.SaveScreenshot(stream, ViewModel.NormalizedFileName);
            await ScreenshotTextIndex.StoreAsync(item.FilePath, png);
            await ViewModel.MarkSavedAsync(item);
            Markup.CanEdit = false;
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void AddToFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is null)
        {
            return;
        }

        var folders = App.MediaLibrary.GetFolders()
            .Where(folder => !folder.IsSystem)
            .Select(folder => folder.Name)
            .ToList();
        var combo = new ComboBox
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ItemsSource = folders,
            PlaceholderText = folders.Count == 0 ? "No folders yet" : "Choose a folder"
        };
        if (folders.Count > 0)
        {
            combo.SelectedIndex = 0;
        }

        var nameBox = new TextBox { PlaceholderText = "New folder name" };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock
        {
            Text = "A copy of the membership is added. The file stays in Screenshots.",
            TextWrapping = TextWrapping.Wrap
        });
        if (folders.Count > 0)
        {
            panel.Children.Add(combo);
            panel.Children.Add(new TextBlock { Text = "Or create a folder:" });
        }

        panel.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            Title = "Copy to folder",
            Content = panel,
            PrimaryButtonText = "Copy",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            string folderName;
            if (!string.IsNullOrWhiteSpace(nameBox.Text))
            {
                folderName = App.MediaLibrary.CreateFolder(nameBox.Text);
            }
            else if (combo.SelectedItem is string selected)
            {
                folderName = selected;
            }
            else
            {
                ViewModel.ShowStatus("Choose an existing folder or type a new name.", InfoBarSeverity.Warning);
                return;
            }

            App.MediaLibrary.AddToFolder(ViewModel.Item.FilePath, folderName);
            await ViewModel.RefreshAsync();
            ViewModel.ShowStatus($"Also in '{folderName}'. Original stays in Screenshots.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is null)
        {
            return;
        }

        try
        {
            var destination = await FilePickerHelper.PickSavePngAsync(App.MainAppWindow, ViewModel.Item.DisplayName);
            if (destination is null)
            {
                return;
            }

            var source = await StorageFile.GetFileFromPathAsync(ViewModel.Item.FilePath);
            await source.CopyAndReplaceAsync(destination);
            ViewModel.ShowStatus($"Copied to {destination.Path}. Original stays in Screenshots.", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Item is null || !File.Exists(ViewModel.Item.FilePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{ViewModel.Item.FilePath}\"",
            UseShellExecute = true
        });
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasPreview)
        {
            return;
        }

        if (!ViewModel.IsSaved)
        {
            ViewModel.Clear();
            Markup.Clear();
            ViewModel.ShowStatus("Screenshot discarded. Nothing was saved.", InfoBarSeverity.Informational);
            return;
        }

        if (ViewModel.Item is null)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete screenshot?",
            Content = $"Permanently delete {ViewModel.Item.DisplayName}? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            App.MediaLibrary.DeleteItems([ViewModel.Item.FilePath]);
            ViewModel.Clear();
            Markup.Clear();
            ViewModel.ShowStatus("Screenshot deleted.", InfoBarSeverity.Informational);
        }
        catch (Exception ex)
        {
            ViewModel.ShowStatus(ex.Message, InfoBarSeverity.Error);
        }
    }
}
