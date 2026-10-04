using System.Collections.ObjectModel;
using System.ComponentModel;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Models;
using Windows.UI;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class MergePage : Page, IPlaybackSource
{
    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 0, 120, 212),
        Color.FromArgb(255, 16, 124, 16),
        Color.FromArgb(255, 216, 59, 1),
        Color.FromArgb(255, 135, 100, 184),
        Color.FromArgb(255, 0, 153, 153),
        Color.FromArgb(255, 227, 0, 140)
    ];

    private readonly ObservableCollection<MergeClip> _clips = [];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _merge;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private Media? _media;
    private VideoView? _videoView;
    private MergeClip? _current;
    private string? _queuedPath;
    private long _queuedSeek = -1;
    private int _clipIndex = -1;
    private double _lastVolume = 80;
    private bool _busy;
    private bool _dragging;
    private bool _updatingSlider;
    private bool _ended;
    private bool _advance;
    private int _playGeneration;
    private MergeClip? _handleClip;
    private bool _handleStart;
    private Grid? _handleHost;
    private readonly List<ClipLane> _lanes = [];
    private long _seekHoldMs = -1;
    private int _seekHoldTicks;
    private bool _holdPause;
    private bool _pausedForOther;
    private bool _dirty;
    private bool _allowLeave;
    private bool _closeWindow;
    private bool _pendingIsBack;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private int _color;
    private string? _busyMessage;
    private bool _showingFades;

    public MergePage()
    {
        InitializeComponent();
        ClipList.ItemsSource = _clips;
        Playback.SpeedCombo.Visibility = Visibility.Collapsed;
        Playback.FullScreenButton.Visibility = Visibility.Collapsed;
        var remembered = PlaybackVolume.Load();
        _lastVolume = remembered.Audible;
        Playback.VolumeSlider.Value = remembered.Level;
        ApplyVolume();
        Playback.MuteButton.Click += Mute_Click;
        Playback.VolumeSlider.ValueChanged += Volume_Changed;
        Playback.PlayButton.Click += Play_Click;
        Playback.BackButton.Click += (_, _) => Skip(-10_000);
        Playback.ForwardButton.Click += (_, _) => Skip(10_000);
        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(Seek_Pressed), true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.AddHandler(PointerCaptureLostEvent, new PointerEventHandler(Seek_Released), true);
        Playback.SeekSlider.ValueChanged += Seek_Changed;
        _timer.Tick += (_, _) => UpdateClock();
        PlaybackFocus.Register(this);
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (_allowLeave || !HasWork)
        {
            return true;
        }

        ShowLeavePrompt();
        return false;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || !HasWork)
        {
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == Microsoft.UI.Xaml.Navigation.NavigationMode.Back;
        if (LeavePrompt.Visibility != Visibility.Visible)
        {
            DispatcherQueue.TryEnqueue(ShowLeavePrompt);
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        ReleasePlayer();
        base.OnNavigatedFrom(e);
    }

    private bool HasWork => _merge is not null || (_dirty && _clips.Count > 0);

    internal bool TryHandleHostClose()
    {
        if (_allowLeave || !HasWork)
        {
            return false;
        }

        _closeWindow = true;
        DispatcherQueue.TryEnqueue(ShowLeavePrompt);
        return true;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            return;
        }

        var files = await ChooseVideosAsync();
        if (files.Count == 0)
        {
            return;
        }

        if (_clips.Count + files.Count > 24)
        {
            Show(InfoBarSeverity.Error, "You can join up to 24 videos at once.");
            return;
        }

        var startPreview = _clips.Count == 0;
        SetBusy(true, "Reading the videos…");
        try
        {
            foreach (var file in files)
            {
                try
                {
                    var source = await VideoMerger.ProbeAsync(file);
                    _clips.Add(new MergeClip(source, NextSwatch(), _clips.Count + 1));
                }
                catch (Exception ex)
                {
                    Show(InfoBarSeverity.Warning, ex.Message);
                }
            }
        }
        finally
        {
            SetBusy(false, null);
            if (_clips.Count > 0)
            {
                _dirty = true;
            }

            Refresh();
            if (startPreview && _clips.Count > 0)
            {
                OpenClip(0, 0);
            }
            else
            {
                ShowTrim();
            }
        }
    }

    private async void Merge_Click(object sender, RoutedEventArgs e) => await SaveMergedAsync();

    private async Task<bool> SaveMergedAsync()
    {
        if (_busy || _clips.Count < 2)
        {
            if (_clips.Count < 2)
            {
                Show(InfoBarSeverity.Error, "Add at least two videos before saving.");
            }

            return false;
        }

        var choice = await AskSaveAsync();
        if (choice is null)
        {
            return false;
        }

        string destination;
        var temporary = false;
        if (choice.OnPc)
        {
            var picked = await FilePickerHelper.PickSaveVideoAsync(App.MainAppWindow, choice.Name);
            if (picked is null)
            {
                return false;
            }

            destination = picked.Path;
        }
        else
        {
            destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");
            temporary = true;
        }

        _merge = new CancellationTokenSource();
        SetBusy(true, "Joining the videos…");
        try
        {
            _player?.SetPause(true);
            await VideoMerger.MergeAsync(_clips.Select(clip => clip.ForMerge()).ToArray(), destination, _merge.Token);
            var savedTo = destination;
            if (!choice.OnPc)
            {
                await using var input = File.OpenRead(destination);
                App.MediaLibrary.ImportMedia(input, choice.Name + ".mp4", choice.Album);
                savedTo = string.IsNullOrWhiteSpace(choice.Album) ? "the library" : choice.Album!;
            }

            _dirty = false;
            Show(InfoBarSeverity.Success, "Saved the video to " + savedTo);
            return true;
        }
        catch (OperationCanceledException)
        {
            Show(InfoBarSeverity.Informational, "The merge was cancelled.");
            return false;
        }
        catch (Exception ex)
        {
            Show(InfoBarSeverity.Error, ex.Message);
            return false;
        }
        finally
        {
            if (temporary && File.Exists(destination))
            {
                try
                {
                    File.Delete(destination);
                }
                catch (IOException)
                {
                }
            }

            _merge.Dispose();
            _merge = null;
            SetBusy(false, null);
        }
    }

    private void ShowLeavePrompt()
    {
        var joining = _merge is not null;
        LeaveTitle.Text = joining
            ? "These videos are still being joined."
            : "These videos are not saved as one file.";
        LeaveDetail.Text = joining
            ? "Stay until the save finishes, or discard this merge."
            : "Save them before leaving, or discard this merge.";
        LeaveSaveButton.Visibility = joining ? Visibility.Collapsed : Visibility.Visible;
        LeavePrompt.Visibility = Visibility.Visible;
    }

    private void LeaveStay_Click(object sender, RoutedEventArgs e)
    {
        _closeWindow = false;
        LeavePrompt.Visibility = Visibility.Collapsed;
    }

    private async void LeaveSave_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        if (await SaveMergedAsync())
        {
            FinishLeave();
        }
    }

    private void LeaveDiscard_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        _merge?.Cancel();
        _dirty = false;
        FinishLeave();
    }

    private void FinishLeave()
    {
        _allowLeave = true;
        if (_closeWindow)
        {
            _closeWindow = false;
            App.MainAppWindow.Close();
            return;
        }

        var moved = false;
        if (_pendingIsBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            moved = true;
        }
        else if (_pendingPageType is not null && NavigationHelper.Follow(Frame, _pendingPageType, _pendingParameter, clearBackStack: true))
        {
            Frame.BackStack.Clear();
            moved = true;
        }

        if (moved && App.MainAppWindow is MainWindow window)
        {
            window.SyncNavigationSelection();
        }
    }

    private async Task<IReadOnlyList<string>> ChooseVideosAsync()
    {
        var method = new ComboBox
        {
            Header = "Add from",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "This PC", "A folder in this app" },
            SelectedIndex = 0
        };
        var folder = new ComboBox
        {
            Header = "App folder",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            DisplayMemberPath = nameof(FolderChoice.Label),
            Visibility = Visibility.Collapsed
        };
        folder.Items.Add(new FolderChoice("Library", null));
        folder.Items.Add(new FolderChoice("Videos", LibraryFolder.Videos));
        folder.Items.Add(new FolderChoice("Recordings", LibraryFolder.Recordings));
        foreach (var album in App.MediaLibrary.GetFolders().Where(item => !item.IsSystem))
        {
            folder.Items.Add(new FolderChoice(album.Name, album.Name));
        }

        folder.SelectedIndex = 0;
        var empty = new TextBlock
        {
            Text = "This folder has no videos.",
            Visibility = Visibility.Collapsed
        };
        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Multiple,
            DisplayMemberPath = nameof(MediaItem.DisplayName),
            Height = 280,
            Visibility = Visibility.Collapsed
        };
        void LoadFolder()
        {
            list.Items.Clear();
            if (folder.SelectedItem is not FolderChoice choice)
            {
                return;
            }

            foreach (var item in App.MediaLibrary.GetItems(choice.Folder).Where(IsMergeVideo))
            {
                list.Items.Add(item);
            }

            empty.Visibility = list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        method.SelectionChanged += (_, _) =>
        {
            var inApp = method.SelectedIndex == 1;
            folder.Visibility = inApp ? Visibility.Visible : Visibility.Collapsed;
            list.Visibility = inApp ? Visibility.Visible : Visibility.Collapsed;
            empty.Visibility = inApp && list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (inApp)
            {
                LoadFolder();
            }
        };
        folder.SelectionChanged += (_, _) => LoadFolder();
        var dialog = new ContentDialog
        {
            Title = "Add videos",
            Content = new StackPanel
            {
                Spacing = 12,
                Width = 420,
                Children = { method, folder, empty, list }
            },
            PrimaryButtonText = "Continue",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return [];
        }

        if (method.SelectedIndex == 0)
        {
            var picked = await FilePickerHelper.PickVideosAsync(App.MainAppWindow);
            return picked.Select(file => file.Path).ToArray();
        }

        return list.SelectedItems.OfType<MediaItem>().Select(item => item.FilePath).Where(File.Exists).ToArray();
    }

    private static bool IsMergeVideo(MediaItem item)
    {
        if (!item.IsVideo || string.IsNullOrWhiteSpace(item.FilePath) || !File.Exists(item.FilePath))
        {
            return false;
        }

        var extension = Path.GetExtension(item.FilePath);
        return extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mkv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mov", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".avi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wmv", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webm", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4v", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record FolderChoice(string Label, string? Folder);

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(sender, 1);

    private void Duplicate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MergeClip clip)
        {
            return;
        }

        if (_clips.Count >= 24)
        {
            Show(InfoBarSeverity.Error, "You can join up to 24 videos at once.");
            return;
        }

        var index = _clips.IndexOf(clip);
        if (index < 0)
        {
            return;
        }

        _clips.Insert(index + 1, clip.Copy(NextSwatch(), index + 2));
        _dirty = true;
        if (_current is not null)
        {
            _clipIndex = _clips.IndexOf(_current);
        }

        Refresh();
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MergeClip clip)
        {
            return;
        }

        var index = _clips.IndexOf(clip);
        var current = clip == _current;
        _clips.Remove(clip);
        _dirty = _clips.Count > 0;
        Refresh();
        if (!current)
        {
            _clipIndex = _current is null ? -1 : _clips.IndexOf(_current);
            return;
        }

        if (_clips.Count == 0)
        {
            ReleasePlayer();
            return;
        }

        OpenClip(Math.Min(index, _clips.Count - 1), 0);
    }

    private void Clip_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MergeClip clip)
        {
            var index = _clips.IndexOf(clip);
            if (index >= 0)
            {
                OpenClip(index, 0);
            }
        }
    }

    private void Clips_Reordered(ListViewBase sender, DragItemsCompletedEventArgs e)
    {
        _dirty = _clips.Count > 0;
        Refresh();
    }

    private void Move(object sender, int direction)
    {
        if ((sender as FrameworkElement)?.DataContext is not MergeClip clip)
        {
            return;
        }

        var index = _clips.IndexOf(clip);
        var next = index + direction;
        if (index < 0 || next < 0 || next >= _clips.Count)
        {
            return;
        }

        _clips.Move(index, next);
        _dirty = true;
        Refresh();
        if (_current is not null)
        {
            _clipIndex = _clips.IndexOf(_current);
        }
    }

    private void Refresh()
    {
        var total = _clips.Sum(clip => clip.KeptSeconds);
        for (var index = 0; index < _clips.Count; index++)
        {
            _clips[index].Order = (index + 1).ToString();
        }

        SummaryText.Text = _clips.Count switch
        {
            0 => "No videos yet",
            1 => "1 video · " + Format(total) + " · add one more",
            _ => $"{_clips.Count} videos · {Format(total)}"
        };
        PreviewHint.Visibility = _clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipList.Visibility = _clips.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var totalMs = TotalMs();
        Playback.DurationText.Text = totalMs > 0 ? FormatMs(totalMs) : "--:--";
        Playback.SeekSlider.IsEnabled = totalMs > 0;
        MergeButton.IsEnabled = _clips.Count >= 2 && !_busy;
        DrawTimeline(total);
        ShowTrim();
    }

    private void DrawTimeline(double total)
    {
        if (_handleClip is not null)
        {
            return;
        }

        TimelineBar.Children.Clear();
        TimelineBar.ColumnDefinitions.Clear();
        _lanes.Clear();
        if (_clips.Count == 0 || total <= 0)
        {
            return;
        }

        for (var index = 0; index < _clips.Count; index++)
        {
            var clip = _clips[index];
            TimelineBar.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(Math.Max(0.1, clip.Source.DurationSeconds), GridUnitType.Star)
            });
            var host = new Grid { Margin = new Thickness(index == 0 ? 0 : 2, 0, 0, 0) };
            host.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            host.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
            var badge = new Border
            {
                Height = 16,
                MinWidth = 16,
                Margin = new Thickness(0, 0, 0, 4),
                Padding = new Thickness(4, 0, 4, 0),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Background = clip.Swatch,
                CornerRadius = new CornerRadius(8),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Colors.White),
                    Text = (index + 1).ToString()
                }
            };
            var track = new Grid();
            var head = new ColumnDefinition();
            var kept = new ColumnDefinition();
            var tail = new ColumnDefinition();
            track.ColumnDefinitions.Add(head);
            track.ColumnDefinitions.Add(kept);
            track.ColumnDefinitions.Add(tail);
            var dim = new SolidColorBrush(clip.Swatch.Color) { Opacity = 0.28 };
            var headBody = new Border { Background = dim };
            var tailBody = new Border { Background = dim };
            var keptBody = new Border { Background = clip.Swatch };
            var leftHandle = Handle();
            var rightHandle = Handle();
            leftHandle.HorizontalAlignment = HorizontalAlignment.Left;
            rightHandle.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(headBody, 0);
            Grid.SetColumn(keptBody, 1);
            Grid.SetColumn(leftHandle, 1);
            Grid.SetColumn(rightHandle, 1);
            Grid.SetColumn(tailBody, 2);
            track.Children.Add(headBody);
            track.Children.Add(keptBody);
            track.Children.Add(tailBody);
            track.Children.Add(leftHandle);
            track.Children.Add(rightHandle);
            Grid.SetRow(track, 1);
            host.Children.Add(badge);
            host.Children.Add(track);
            var lane = new ClipLane(clip, host, head, kept, tail);
            FitLane(lane);
            var clipIndex = index;
            leftHandle.PointerPressed += (_, args) => BeginHandle(clip, start: true, host, args);
            rightHandle.PointerPressed += (_, args) => BeginHandle(clip, start: false, host, args);
            host.PointerMoved += (_, args) => MoveHandle(host, args);
            host.PointerReleased += (_, args) => EndHandle(host, args);
            host.PointerCanceled += (_, args) => EndHandle(host, args);
            keptBody.PointerReleased += (_, args) =>
            {
                if (_handleClip is not null)
                {
                    return;
                }

                var point = args.GetCurrentPoint(host).Position.X;
                var seconds = point / Math.Max(1, host.ActualWidth) * clip.Source.DurationSeconds;
                seconds = Math.Clamp(seconds, clip.StartSeconds, clip.EndSeconds);
                var offset = (long)Math.Max(0, (seconds - clip.StartSeconds) * 1000);
                HoldSeek(SumBefore(clipIndex) + offset);
                OpenClip(clipIndex, offset);
                args.Handled = true;
            };
            ToolTipService.SetToolTip(host, clip.Name);
            Grid.SetColumn(host, index);
            TimelineBar.Children.Add(host);
            _lanes.Add(lane);
        }
    }

    private static Border Handle() => new()
    {
        Width = 12,
        Height = 22,
        Background = new SolidColorBrush(Colors.White),
        CornerRadius = new CornerRadius(3),
        VerticalAlignment = VerticalAlignment.Center
    };

    private void BeginHandle(MergeClip clip, bool start, Grid host, PointerRoutedEventArgs args)
    {
        _handleClip = clip;
        _handleStart = start;
        _handleHost = host;
        host.CapturePointer(args.Pointer);
        MoveHandle(host, args);
        args.Handled = true;
    }

    private void MoveHandle(Grid host, PointerRoutedEventArgs args)
    {
        if (_handleClip is null || _handleHost != host)
        {
            return;
        }

        var seconds = args.GetCurrentPoint(host).Position.X / Math.Max(1, host.ActualWidth) * _handleClip.Source.DurationSeconds;
        if (_handleStart)
        {
            ApplyTrim(_handleClip, seconds, _handleClip.EndSeconds, redraw: false);
        }
        else
        {
            ApplyTrim(_handleClip, _handleClip.StartSeconds, seconds, redraw: false);
        }

        args.Handled = true;
    }

    private void EndHandle(Grid host, PointerRoutedEventArgs args)
    {
        if (_handleHost != host)
        {
            return;
        }

        _handleClip = null;
        _handleHost = null;
        host.ReleasePointerCapture(args.Pointer);
        Refresh();
        args.Handled = true;
    }

    private static void FitLane(ClipLane lane)
    {
        var duration = Math.Max(0.1, lane.Clip.Source.DurationSeconds);
        lane.Head.Width = new GridLength(Math.Max(0, lane.Clip.StartSeconds), GridUnitType.Star);
        lane.Kept.Width = new GridLength(Math.Max(0.1, lane.Clip.KeptSeconds), GridUnitType.Star);
        lane.Tail.Width = new GridLength(Math.Max(0, duration - lane.Clip.EndSeconds), GridUnitType.Star);
    }

    private async Task<MergeSaveChoice?> AskSaveAsync()
    {
        var nameBox = new TextBox { Header = "Name", Text = "Merged video" };
        var method = new ComboBox
        {
            Header = "Save to",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Items = { "A folder in this app", "A folder on this PC" },
            SelectedIndex = 0
        };
        var album = new ComboBox
        {
            Header = "App folder",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        album.Items.Add("Library");
        foreach (var folder in App.MediaLibrary.GetFolders().Where(folder => !folder.IsSystem))
        {
            album.Items.Add(folder.Name);
        }

        album.Items.Add("New folder…");
        album.SelectedIndex = 0;
        var newFolder = new TextBox { Header = "New folder name", Visibility = Visibility.Collapsed };
        album.SelectionChanged += (_, _) =>
        {
            newFolder.Visibility = album.SelectedItem as string == "New folder…" ? Visibility.Visible : Visibility.Collapsed;
        };
        method.SelectionChanged += (_, _) =>
        {
            var inApp = method.SelectedIndex == 0;
            album.Visibility = inApp ? Visibility.Visible : Visibility.Collapsed;
            newFolder.Visibility = inApp && album.SelectedItem as string == "New folder…" ? Visibility.Visible : Visibility.Collapsed;
        };
        var dialog = new ContentDialog
        {
            Title = "Save merged video",
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { nameBox, method, album, newFolder }
            },
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var name = CleanName(nameBox.Text);
        if (method.SelectedIndex == 1)
        {
            return new MergeSaveChoice(name, true, null);
        }

        if (album.SelectedItem as string == "New folder…")
        {
            if (string.IsNullOrWhiteSpace(newFolder.Text))
            {
                Show(InfoBarSeverity.Error, "Enter a folder name.");
                return null;
            }

            try
            {
                return new MergeSaveChoice(name, false, App.MediaLibrary.CreateFolder(newFolder.Text));
            }
            catch (Exception ex)
            {
                Show(InfoBarSeverity.Error, ex.Message);
                return null;
            }
        }

        var selected = album.SelectedItem as string;
        return new MergeSaveChoice(name, false, selected == "Library" ? null : selected);
    }

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        MergeButton.IsEnabled = !busy && _clips.Count >= 2;
        if (busy && message is not null)
        {
            _busyMessage = message;
            Show(InfoBarSeverity.Informational, message);
            return;
        }

        if (_busyMessage is not null && StatusBar.Message == _busyMessage)
        {
            StatusBar.IsOpen = false;
        }

        _busyMessage = null;
    }

    private void OpenClip(int index, long offsetMs)
    {
        if (index < 0 || index >= _clips.Count)
        {
            return;
        }

        _ended = false;
        _advance = false;
        _playGeneration++;
        _clipIndex = index;
        _current = _clips[index];
        _queuedPath = _current.Source.Path;
        var keptOffset = Math.Clamp(offsetMs, 0, Math.Max(0, ClipMs(_current) - 50));
        _queuedSeek = _current.StartMs + keptOffset;
        PreviewHint.Visibility = Visibility.Collapsed;
        ShowTrim();
        if (_player is null)
        {
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AttachVideo);
            return;
        }

        PlayPath(_queuedPath);
    }

    private void AttachVideo()
    {
        if (_videoView is null)
        {
            _videoView = new VideoView
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            _videoView.Initialized += VideoView_Initialized;
        }

        VideoHost.Child = _videoView;
    }

    private void VideoView_Initialized(object? sender, InitializedEventArgs e)
    {
        _libVlc = new LibVLC(false, PlaybackAudio.Options(e.SwapChainOptions));
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += (_, args) => DispatcherQueue.TryEnqueue(() => ApplyPendingSeek(args.Length));
        _player.Playing += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (_player is null)
            {
                return;
            }

            if (_pausedForOther || _holdPause)
            {
                try
                {
                    _player.SetPause(true);
                }
                catch (Exception)
                {
                    // Playback already moved to another video.
                }

                _holdPause = false;
                UpdatePlayIcon();
                return;
            }

            PlaybackFocus.Claim(this);
            UpdatePlayIcon();
        });
        _player.Paused += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayIcon);
        _player.EndReached += (_, _) =>
        {
            var generation = _playGeneration;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (generation == _playGeneration && _queuedSeek < 0)
                {
                    _advance = true;
                }
            });
        };
        _videoView!.MediaPlayer = _player;
        ApplyVolume();
        _timer.Start();
        if (_queuedPath is not null)
        {
            PlayPath(_queuedPath);
        }
    }

    private void PlayPath(string path)
    {
        if (_libVlc is null || _player is null)
        {
            return;
        }

        _ended = false;
        _media?.Dispose();
        _media = new Media(_libVlc, path, FromType.FromPath);
        if (_pausedForOther)
        {
            return;
        }

        if (!_holdPause)
        {
            TakePlayback();
        }

        _player.Play(_media);
        UpdatePlayIcon();
    }

    private void TakePlayback()
    {
        _pausedForOther = false;
        PlaybackFocus.Claim(this);
    }

    void IPlaybackSource.PauseForOther()
    {
        _pausedForOther = true;
        if (_player is not { IsPlaying: true })
        {
            UpdatePlayIcon();
            return;
        }

        try
        {
            _player.SetPause(true);
        }
        catch (Exception)
        {
            // The preview can already be stopped when another video starts.
        }

        UpdatePlayIcon();
    }

    private void ApplyPendingSeek(long length)
    {
        if (_player is null || _queuedSeek < 0)
        {
            return;
        }

        var offset = length > 0 ? Math.Min(_queuedSeek, Math.Max(0, length - 50)) : _queuedSeek;
        _queuedSeek = -1;
        _player.Time = offset;
        if (_holdPause)
        {
            _player.SetPause(true);
            _holdPause = false;
        }
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_clips.Count == 0)
        {
            return;
        }

        if (_ended || _clipIndex < 0 || _player is null)
        {
            _pausedForOther = false;
            OpenClip(0, 0);
            return;
        }

        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else
        {
            TakePlayback();
            _player.Play();
        }

        UpdatePlayIcon();
    }

    private void Skip(long delta)
    {
        if (_clips.Count == 0)
        {
            return;
        }

        SeekGlobal(Math.Clamp(GlobalMs() + delta, 0, Math.Max(0, TotalMs() - 50)));
    }

    private void Seek_Pressed(object sender, PointerRoutedEventArgs e)
    {
        if (TotalMs() > 0)
        {
            _dragging = true;
        }
    }

    private void Seek_Released(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        var total = TotalMs();
        if (total <= 0 || Playback.SeekSlider.Maximum <= 0)
        {
            return;
        }

        SeekGlobal((long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * total));
    }

    private void Seek_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || !_dragging)
        {
            return;
        }

        var total = TotalMs();
        if (total > 0)
        {
            Playback.PositionText.Text = FormatMs((long)(e.NewValue / Playback.SeekSlider.Maximum * total));
            ApplyPreviewFade();
        }
    }

    private void SeekGlobal(long milliseconds)
    {
        long walked = 0;
        for (var index = 0; index < _clips.Count; index++)
        {
            var length = ClipMs(_clips[index]);
            if (milliseconds < walked + length || index == _clips.Count - 1)
            {
                var offset = Math.Clamp(milliseconds - walked, 0, Math.Max(0, length - 50));
                HoldSeek(walked + offset);
                var paused = _player is not null && !_player.IsPlaying;
                if (index == _clipIndex && _current == _clips[index] && _player is not null && _queuedSeek < 0)
                {
                    _ended = false;
                    _advance = false;
                    _holdPause = paused;
                    _player.Time = _clips[index].StartMs + offset;
                    if (paused)
                    {
                        _player.SetPause(true);
                    }
                }
                else
                {
                    _holdPause = paused;
                    OpenClip(index, offset);
                }

                return;
            }

            walked += length;
        }
    }

    private void UpdateClock()
    {
        if (_queuedSeek < 0
            && _seekHoldMs < 0
            && _player is not null
            && _current is not null
            && _player.IsPlaying
            && _player.Time >= _current.EndMs - 80)
        {
            _advance = true;
        }

        if (_advance)
        {
            _advance = false;
            if (_clipIndex + 1 < _clips.Count)
            {
                OpenClip(_clipIndex + 1, 0);
            }
            else
            {
                _ended = true;
                UpdatePlayIcon();
            }

            return;
        }

        if (_player is null || _dragging)
        {
            return;
        }

        var total = TotalMs();
        var position = _seekHoldMs >= 0 ? _seekHoldMs : GlobalMs();
        if (_seekHoldMs >= 0 && (Math.Abs(GlobalMs() - _seekHoldMs) < 400 || --_seekHoldTicks <= 0))
        {
            _seekHoldMs = -1;
            position = GlobalMs();
        }

        Playback.PositionText.Text = FormatMs(position);
        if (total > 0)
        {
            _updatingSlider = true;
            Playback.SeekSlider.Value = Math.Clamp(position / (double)total * Playback.SeekSlider.Maximum, 0, Playback.SeekSlider.Maximum);
            _updatingSlider = false;
        }

        UpdatePlayIcon();
        ApplyPreviewFade();
    }

    private long GlobalMs()
    {
        long prefix = 0;
        for (var index = 0; index < _clipIndex && index < _clips.Count; index++)
        {
            prefix += ClipMs(_clips[index]);
        }

        var into = Math.Max(0, (_player?.Time ?? _current?.StartMs ?? 0) - (_current?.StartMs ?? 0));
        if (_current is not null)
        {
            into = Math.Min(into, ClipMs(_current));
        }

        return prefix + into;
    }

    private long SumBefore(int index)
    {
        long prefix = 0;
        for (var i = 0; i < index && i < _clips.Count; i++)
        {
            prefix += ClipMs(_clips[i]);
        }

        return prefix;
    }

    private void HoldSeek(long milliseconds)
    {
        _seekHoldMs = Math.Clamp(milliseconds, 0, Math.Max(0, TotalMs() - 50));
        _seekHoldTicks = 8;
        var total = TotalMs();
        if (total <= 0)
        {
            return;
        }

        _updatingSlider = true;
        Playback.SeekSlider.Value = Math.Clamp(_seekHoldMs / (double)total * Playback.SeekSlider.Maximum, 0, Playback.SeekSlider.Maximum);
        _updatingSlider = false;
        Playback.PositionText.Text = FormatMs(_seekHoldMs);
    }

    private long TotalMs() => _clips.Sum(ClipMs);

    private static long ClipMs(MergeClip clip) => Math.Max(100, clip.EndMs - clip.StartMs);

    private void ShowTrim()
    {
        if (TrimCard is null)
        {
            return;
        }

        if (_current is null)
        {
            TrimCard.Visibility = Visibility.Collapsed;
            return;
        }

        TrimCard.Visibility = Visibility.Visible;
        TrimName.Text = _current.Name;
        if (TrimStartBox.FocusState == FocusState.Unfocused)
        {
            TrimStartBox.Text = Format(_current.StartSeconds);
        }

        if (TrimEndBox.FocusState == FocusState.Unfocused)
        {
            TrimEndBox.Text = Format(_current.EndSeconds);
        }

        _showingFades = true;
        VideoFadeInBox.IsChecked = _current.VideoFadeIn;
        VideoFadeOutBox.IsChecked = _current.VideoFadeOut;
        AudioFadeInBox.IsChecked = _current.AudioFadeIn;
        AudioFadeOutBox.IsChecked = _current.AudioFadeOut;
        VideoFadeInSeconds.Value = _current.VideoFadeInSeconds;
        VideoFadeOutSeconds.Value = _current.VideoFadeOutSeconds;
        AudioFadeInSeconds.Value = _current.AudioFadeInSeconds;
        AudioFadeOutSeconds.Value = _current.AudioFadeOutSeconds;
        VideoFadeInSeconds.IsEnabled = _current.VideoFadeIn;
        VideoFadeOutSeconds.IsEnabled = _current.VideoFadeOut;
        AudioFadeInSeconds.IsEnabled = _current.AudioFadeIn;
        AudioFadeOutSeconds.IsEnabled = _current.AudioFadeOut;
        _showingFades = false;
    }

    private void Fade_Changed(object sender, RoutedEventArgs e) => ReadFades();

    private void Fade_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args) => ReadFades();

    private void ReadFades()
    {
        if (_showingFades
            || _current is null
            || VideoFadeInBox is null
            || VideoFadeOutBox is null
            || AudioFadeInBox is null
            || AudioFadeOutBox is null
            || VideoFadeInSeconds is null
            || VideoFadeOutSeconds is null
            || AudioFadeInSeconds is null
            || AudioFadeOutSeconds is null)
        {
            return;
        }

        VideoFadeInSeconds.IsEnabled = VideoFadeInBox.IsChecked == true;
        VideoFadeOutSeconds.IsEnabled = VideoFadeOutBox.IsChecked == true;
        AudioFadeInSeconds.IsEnabled = AudioFadeInBox.IsChecked == true;
        AudioFadeOutSeconds.IsEnabled = AudioFadeOutBox.IsChecked == true;
        _current.SetFades(
            VideoFadeInBox.IsChecked == true,
            FadeSeconds(VideoFadeInSeconds),
            VideoFadeOutBox.IsChecked == true,
            FadeSeconds(VideoFadeOutSeconds),
            AudioFadeInBox.IsChecked == true,
            FadeSeconds(AudioFadeInSeconds),
            AudioFadeOutBox.IsChecked == true,
            FadeSeconds(AudioFadeOutSeconds));
        _dirty = true;
        ApplyPreviewFade();
    }

    private static double FadeSeconds(NumberBox box)
    {
        var value = box.Value;
        if (double.IsNaN(value) || value < 0.1)
        {
            return 1;
        }

        return Math.Min(10, value);
    }

    private void ApplyPreviewFade()
    {
        var clip = _current;
        var into = 0d;
        if (clip is not null && _player is not null)
        {
            into = Math.Max(0, (_player.Time - clip.StartMs) / 1000d);
        }

        if (_dragging && clip is not null)
        {
            var total = TotalMs();
            if (total > 0)
            {
                var global = Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * total;
                into = IntoSeconds(global);
                clip = ClipAt(global) ?? clip;
            }
        }

        var picture = clip is null ? 1 : FadeLevel(clip.VideoFadeIn, clip.VideoFadeInSeconds, clip.VideoFadeOut, clip.VideoFadeOutSeconds, into, clip.KeptSeconds);
        var sound = clip is null ? 1 : FadeLevel(clip.AudioFadeIn, clip.AudioFadeInSeconds, clip.AudioFadeOut, clip.AudioFadeOutSeconds, into, clip.KeptSeconds);
        if (VideoHost is not null)
        {
            VideoHost.Opacity = picture;
        }

        if (_player is not null)
        {
            var listen = Playback.VolumeSlider.Value * sound;
            _player.Mute = listen <= 0.5;
            _player.Volume = (int)Math.Round(listen);
        }
    }

    private MergeClip? ClipAt(double globalMs)
    {
        double walked = 0;
        foreach (var clip in _clips)
        {
            var length = ClipMs(clip);
            if (globalMs < walked + length)
            {
                return clip;
            }

            walked += length;
        }

        return _clips.LastOrDefault();
    }

    private double IntoSeconds(double globalMs)
    {
        double walked = 0;
        foreach (var clip in _clips)
        {
            var length = ClipMs(clip);
            if (globalMs < walked + length)
            {
                return Math.Max(0, (globalMs - walked) / 1000d);
            }

            walked += length;
        }

        return 0;
    }

    private static double FadeLevel(bool fadeIn, double inSeconds, bool fadeOut, double outSeconds, double time, double total)
    {
        var level = 1d;
        if (fadeIn && inSeconds > 0 && total > 0)
        {
            var length = Math.Min(inSeconds, total);
            if (time < length)
            {
                level = Math.Min(level, time / length);
            }
        }

        if (fadeOut && outSeconds > 0 && total > 0)
        {
            var length = Math.Min(outSeconds, total);
            if (time > total - length)
            {
                level = Math.Min(level, Math.Max(0, total - time) / length);
            }
        }

        return level;
    }

    private void TrimTime_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            ApplyTrimBoxes();
            e.Handled = true;
        }
    }

    private void TrimTime_LostFocus(object sender, RoutedEventArgs e) => ApplyTrimBoxes();

    private void TrimStartHere_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _player is null || _clipIndex < 0 || _clips[_clipIndex] != _current)
        {
            return;
        }

        ApplyTrim(_current, _player.Time / 1000d, _current.EndSeconds);
    }

    private void TrimEndHere_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null || _player is null || _clipIndex < 0 || _clips[_clipIndex] != _current)
        {
            return;
        }

        ApplyTrim(_current, _current.StartSeconds, _player.Time / 1000d);
    }

    private void TrimFull_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            return;
        }

        ApplyTrim(_current, 0, _current.Source.DurationSeconds);
    }

    private void ApplyTrimBoxes()
    {
        if (_current is null)
        {
            return;
        }

        if (!TryParseClock(TrimStartBox.Text, out var start) || !TryParseClock(TrimEndBox.Text, out var end))
        {
            ShowTrim();
            return;
        }

        ApplyTrim(_current, start, end);
    }

    private void ApplyTrim(MergeClip clip, double start, double end, bool redraw = true)
    {
        var duration = clip.Source.DurationSeconds;
        start = Math.Clamp(start, 0, Math.Max(0, duration - 0.1));
        end = Math.Clamp(end, start + 0.1, duration);
        if (Math.Abs(clip.StartSeconds - start) < 0.02 && Math.Abs(clip.EndSeconds - end) < 0.02)
        {
            ShowTrim();
            return;
        }

        clip.SetSpan(start, end);
        _dirty = true;
        if (!redraw)
        {
            var lane = _lanes.FirstOrDefault(item => item.Clip == clip);
            if (lane is not null)
            {
                FitLane(lane);
            }

            var kept = _clips.Sum(item => item.KeptSeconds);
            SummaryText.Text = _clips.Count == 1
                ? "1 video · " + Format(kept) + " · add one more"
                : $"{_clips.Count} videos · {Format(kept)}";
            Playback.DurationText.Text = FormatMs(TotalMs());
            ShowTrim();
        }
        else
        {
            Refresh();
        }

        if (clip == _current && _player is not null)
        {
            var time = _player.Time / 1000d;
            if (time < start - 0.05 || time > end - 0.05)
            {
                _holdPause = !_player.IsPlaying;
                _player.Time = clip.StartMs;
            }
        }
    }

    private static bool TryParseClock(string text, out double seconds)
    {
        seconds = 0;
        var parts = text.Trim().Split(':');
        if (parts.Length == 1 && double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var plain))
        {
            seconds = plain;
            return true;
        }

        if (parts.Length == 2
            && int.TryParse(parts[0], out var minutes)
            && double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs))
        {
            seconds = minutes * 60 + secs;
            return true;
        }

        if (parts.Length == 3
            && int.TryParse(parts[0], out var hours)
            && int.TryParse(parts[1], out minutes)
            && double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out secs))
        {
            seconds = hours * 3600 + minutes * 60 + secs;
            return true;
        }

        return false;
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        if (Playback.VolumeSlider.Value > 0)
        {
            _lastVolume = Playback.VolumeSlider.Value;
            Playback.VolumeSlider.Value = 0;
        }
        else
        {
            Playback.VolumeSlider.Value = _lastVolume <= 0 ? 80 : _lastVolume;
        }
    }

    private void Volume_Changed(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue > 0)
        {
            _lastVolume = e.NewValue;
        }

        PlaybackVolume.Save(e.NewValue);
        ApplyVolume();
    }

    private void ApplyVolume()
    {
        if (_player is not null)
        {
            _player.Mute = Playback.VolumeSlider.Value <= 0;
            _player.Volume = (int)Math.Round(Playback.VolumeSlider.Value);
        }

        if (Playback.MuteIcon is not null)
        {
            Playback.MuteIcon.Glyph = Playback.VolumeSlider.Value <= 0 ? "\uE74F" : "\uE767";
        }
    }

    private void UpdatePlayIcon()
    {
        if (Playback.PlayIcon is not null)
        {
            Playback.PlayIcon.Glyph = _player?.IsPlaying == true ? "\uE769" : "\uE768";
        }
    }

    private void ReleasePlayer()
    {
        _timer.Stop();
        _advance = false;
        _ended = false;
        _current = null;
        _clipIndex = -1;
        _queuedPath = null;
        _queuedSeek = -1;
        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
            }

            if (_videoView is not null)
            {
                _videoView.MediaPlayer = null;
            }

            _player.Dispose();
            _player = null;
        }

        _media?.Dispose();
        _media = null;
        _libVlc?.Dispose();
        _libVlc = null;
        if (VideoHost is not null)
        {
            VideoHost.Child = PreviewHint;
        }

        ShowTrim();
        UpdatePlayIcon();
    }

    private static string FormatMs(long milliseconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private void Show(InfoBarSeverity severity, string message)
    {
        StatusBar.Severity = severity;
        StatusBar.Message = message;
        StatusBar.IsOpen = true;
    }

    private SolidColorBrush NextSwatch()
    {
        var color = Palette[_color % Palette.Length];
        _color++;
        return new SolidColorBrush(color);
    }

    private static string Format(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private static string CleanName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(character => invalid.Contains(character) ? ' ' : character).ToArray());
        cleaned = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(cleaned) ? "Merged video" : cleaned;
    }

    private sealed record MergeSaveChoice(string Name, bool OnPc, string? Album);

    private sealed class ClipLane(MergeClip clip, Grid host, ColumnDefinition head, ColumnDefinition kept, ColumnDefinition tail)
    {
        public MergeClip Clip { get; } = clip;

        public Grid Host { get; } = host;

        public ColumnDefinition Head { get; } = head;

        public ColumnDefinition Kept { get; } = kept;

        public ColumnDefinition Tail { get; } = tail;
    }

    private sealed class MergeClip : INotifyPropertyChanged
    {
        private string _order;

        private double _start;
        private double _end;
        private string _detail;

        public MergeClip(MergeSource source, SolidColorBrush swatch, int order)
        {
            Source = source;
            Swatch = swatch;
            Name = Path.GetFileName(source.Path);
            _order = order.ToString();
            _end = source.DurationSeconds;
            _detail = Describe();
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MergeSource Source { get; }

        public string Name { get; }

        public double StartSeconds => _start;

        public double EndSeconds => _end;

        public double KeptSeconds => Math.Max(0.1, _end - _start);

        public long StartMs => (long)Math.Round(_start * 1000);

        public long EndMs => (long)Math.Round(_end * 1000);

        public string Detail => _detail;

        public bool VideoFadeIn { get; private set; }

        public double VideoFadeInSeconds { get; private set; } = 1;

        public bool VideoFadeOut { get; private set; }

        public double VideoFadeOutSeconds { get; private set; } = 1;

        public bool AudioFadeIn { get; private set; }

        public double AudioFadeInSeconds { get; private set; } = 1;

        public bool AudioFadeOut { get; private set; }

        public double AudioFadeOutSeconds { get; private set; } = 1;

        public MergeSource ForMerge() => Source with
        {
            TrimStartSeconds = _start,
            TrimEndSeconds = _end,
            VideoFadeInSeconds = VideoFadeIn ? VideoFadeInSeconds : 0,
            VideoFadeOutSeconds = VideoFadeOut ? VideoFadeOutSeconds : 0,
            AudioFadeInSeconds = AudioFadeIn ? AudioFadeInSeconds : 0,
            AudioFadeOutSeconds = AudioFadeOut ? AudioFadeOutSeconds : 0
        };

        public MergeClip Copy(SolidColorBrush swatch, int order)
        {
            var copy = new MergeClip(Source, swatch, order);
            copy.SetSpan(_start, _end);
            copy.SetFades(VideoFadeIn, VideoFadeInSeconds, VideoFadeOut, VideoFadeOutSeconds, AudioFadeIn, AudioFadeInSeconds, AudioFadeOut, AudioFadeOutSeconds);
            return copy;
        }

        public void SetFades(bool videoIn, double videoInSeconds, bool videoOut, double videoOutSeconds, bool audioIn, double audioInSeconds, bool audioOut, double audioOutSeconds)
        {
            VideoFadeIn = videoIn;
            VideoFadeInSeconds = videoInSeconds;
            VideoFadeOut = videoOut;
            VideoFadeOutSeconds = videoOutSeconds;
            AudioFadeIn = audioIn;
            AudioFadeInSeconds = audioInSeconds;
            AudioFadeOut = audioOut;
            AudioFadeOutSeconds = audioOutSeconds;
            _detail = Describe();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        }

        public void SetSpan(double start, double end)
        {
            _start = start;
            _end = end;
            _detail = Describe();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));
        }

        private string Describe()
        {
            var sound = Source.HasAudio ? "sound included" : "no audio, silence keeps the next clip in time";
            var fades = VideoFadeIn || VideoFadeOut || AudioFadeIn || AudioFadeOut ? " · fades" : string.Empty;
            return Format(_start) + "–" + Format(_end) + " kept · " + sound + fades;
        }

        public SolidColorBrush Swatch { get; }

        public string Order
        {
            get => _order;
            set
            {
                if (_order == value)
                {
                    return;
                }

                _order = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Order)));
            }
        }
    }
}
