using System.Diagnostics;
using System.Globalization;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Models;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class VideoPlayerPage : Page
{
    private const long MaxRealisticMs = 24L * 60 * 60 * 1000;

    private readonly DispatcherTimer _timer;
    private LibVLC? _libVlc;
    private VlcMediaPlayer? _player;
    private string? _pendingPath;
    private string? _filePath;
    private bool _editing;

    private bool HasTrimChange =>
        _editing && _durationMs > 0 && (_trimStartMs > 50 || _durationMs - _trimEndMs > 50);
    private bool _savingTrim;
    private bool _grabbingFrame;
    private long _trimStartMs;
    private long _trimEndMs;
    private string[]? _swapChainOptions;
    private long _resumeMs;
    private bool _resumePending;
    private bool _ended;
    private long _lastRememberedMs;
    private bool _dragging;
    private bool _updatingSlider;
    private bool _hasValidDuration;
    private long _durationMs;
    private double _lastVolume = 80;
    private bool _transitioning;
    private bool _allowLeave;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;
    private static readonly Thickness WindowedPadding = new(24, 8, 24, 24);

    public VideoPlayerPage()
    {
        InitializeComponent();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => UpdateClockAndBar();

        Playback.SeekSlider.AddHandler(PointerPressedEvent, new PointerEventHandler(OnSeekPressed), handledEventsToo: true);
        Playback.SeekSlider.AddHandler(PointerReleasedEvent, new PointerEventHandler(OnSeekReleased), handledEventsToo: true);
        Playback.SeekSlider.AddHandler(PointerCanceledEvent, new PointerEventHandler(OnSeekReleased), handledEventsToo: true);
        Playback.SeekSlider.ValueChanged += SeekSlider_ValueChanged;
        Playback.VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;
        Playback.MuteButton.Click += MuteButton_Click;
        Playback.BackButton.Click += RewindButton_Click;
        Playback.ForwardButton.Click += ForwardButton_Click;
        Playback.PlayButton.Click += PlayPauseButton_Click;
        Playback.SpeedCombo.SelectionChanged += SpeedCombo_SelectionChanged;
        Playback.FullScreenButton.Click += FullscreenButton_Click;
        Playback.SnapshotButton.Visibility = Visibility.Visible;
        Playback.SnapshotButton.Click += Snapshot_Click;
        Playback.BookmarkButton.Visibility = Visibility.Visible;
        Playback.BookmarkButton.Click += Bookmark_Click;
        Playback.TrimRangeChanged += (_, _) => ApplyTrimFromBar();
        Playback.TrimSeekRequested += (_, fraction) => SeekToFraction(fraction);
        Loaded += (_, _) => Focus(FocusState.Programmatic);
        VideoView.Loaded += (_, _) => EnsurePlayback();
        KeyDown += VideoPlayerPage_KeyDown;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var item = e.Parameter switch
        {
            MediaItem media => App.MediaLibrary.GetById(media.Id) ?? media,
            string id => App.MediaLibrary.GetById(id),
            _ => null
        };

        if (item is null)
        {
            return;
        }

        TitleText.Text = item.DisplayName;
        AddedText.Text = $"Added {item.ImportedAt.ToLocalTime():g}";
        RestoreButton.Visibility = App.MediaLibrary.HasOriginal(item.FilePath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _filePath = item.FilePath;
        if (VideoHost.Child is null)
        {
            VideoHost.Child = VideoView;
        }

        Playback.SetHoverSource(item.FilePath);
        ShowBookmarks();
        ResetDuration();
        _pendingPath = item.FilePath;

        if (_player is not null)
        {
            PlayFile(item.FilePath);
        }

        _timer.Start();
        UpdateClockAndBar();
        UpdatePlayIcon();
        if (HostWindow is not null)
        {
            HostWindow.AppWindow.Changed += AppWindow_Changed;
        }
    }

    internal bool PrepareToLeave(Type? pageType, object? parameter, bool back)
    {
        _pendingPageType = pageType;
        _pendingParameter = parameter;
        _pendingIsBack = back;
        if (!HasTrimChange && !_savingTrim)
        {
            return true;
        }

        ShowLeavePrompt();
        return false;
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || (!HasTrimChange && !_savingTrim))
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

    private void ShowLeavePrompt()
    {
        LeavePromptText.Text = _savingTrim
            ? "A trim is still saving. Stay until it finishes."
            : "This trim is not saved. Leave without keeping it?";
        LeavePromptConfirm.Visibility = _savingTrim ? Visibility.Collapsed : Visibility.Visible;
        LeavePrompt.Visibility = Visibility.Visible;
    }

    private void LeavePromptStay_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
    }

    private void LeavePromptConfirm_Click(object sender, RoutedEventArgs e)
    {
        LeavePrompt.Visibility = Visibility.Collapsed;
        LeaveTrimMode();
        _allowLeave = true;
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

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        if (HostWindow is not null)
        {
            HostWindow.AppWindow.Changed -= AppWindow_Changed;
        }

        _timer.Stop();
        RememberPosition(force: true);
        _pendingPath = null;
        Playback.SetHoverSource(null);
        ExitFullScreen();
        ResetDuration();
        DetachPlayback();
    }

    private void DetachPlayback()
    {
        if (_player is not null)
        {
            _player.LengthChanged -= Player_LengthChanged;
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
                // The player can already be stopped when the page is leaving.
            }

            VideoView.MediaPlayer = null;
            _player.Dispose();
            _player = null;
        }

        _libVlc?.Dispose();
        _libVlc = null;
        if (VideoHost.Child is not null)
        {
            VideoHost.Child = null;
        }
    }

    private void VideoView_Initialized(object sender, InitializedEventArgs e)
    {
        _swapChainOptions = e.SwapChainOptions;
        EnsurePlayback();
    }

    private void EnsurePlayback()
    {
        if (_player is not null || _swapChainOptions is null)
        {
            return;
        }

        _libVlc = new LibVLC(enableDebugLogs: false, _swapChainOptions);
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += Player_LengthChanged;
        HookPlayer();
        VideoView.MediaPlayer = _player;
        ApplyVolumeToPlayer();
        ApplyRateToPlayer();
        if (!string.IsNullOrWhiteSpace(_pendingPath))
        {
            PlayFile(_pendingPath);
        }
    }

    private void PlayFile(string path)
    {
        if (_libVlc is null || _player is null)
        {
            return;
        }

        ResetDuration();
        _resumeMs = PlaybackProgress.Load(path);
        _resumePending = _resumeMs >= 5_000;
        _lastRememberedMs = _resumeMs;
        using var media = new Media(_libVlc, path, FromType.FromPath);
        media.Parse(MediaParseOptions.ParseLocal);
        ApplyDuration(media.Duration, "media-parse");
        _ended = false;
        _player.Play(media);
        ApplyRateToPlayer();
    }

    private void Player_LengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() => ApplyDuration(e.Length, "vlc-length"));
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            _player.SetPause(true);
        }
        else if (_ended && !string.IsNullOrWhiteSpace(_filePath))
        {
            PlayFile(_filePath);
        }
        else
        {
            if (_editing && (_player.Time < _trimStartMs || _player.Time >= _trimEndMs))
            {
                _player.Time = _trimStartMs;
            }

            _player.Play();
        }

        UpdatePlayIcon();
    }

    private void RewindButton_Click(object sender, RoutedEventArgs e) => Skip(-10_000);

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => Skip(10_000);

    private void Skip(long deltaMs)
    {
        if (_player is null)
        {
            return;
        }

        var length = _hasValidDuration && _durationMs > 0 ? _durationMs : Math.Max(_player.Length, 0);
        var min = _editing ? _trimStartMs : 0;
        var max = _editing ? _trimEndMs : length;
        var current = Math.Max(_player.Time, 0);
        var next = current + deltaMs;
        if (next < min)
        {
            next = min;
        }

        if (max > 0 && next > max)
        {
            next = max;
        }

        _player.Time = next;
        UpdateClockAndBar();
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
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

        ApplyVolumeToPlayer();
    }

    private void VolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (e.NewValue > 0)
        {
            _lastVolume = e.NewValue;
        }

        ApplyVolumeToPlayer();
    }

    private void ApplyVolumeToPlayer()
    {
        if (_player is null)
        {
            UpdateMuteIcon();
            return;
        }

        _player.Mute = Playback.VolumeSlider.Value <= 0;
        _player.Volume = (int)Math.Round(Playback.VolumeSlider.Value);
        UpdateMuteIcon();
    }

    private void UpdateMuteIcon()
    {
        Playback.MuteIcon.Glyph = Playback.VolumeSlider.Value <= 0 ? "\uE74F" : "\uE767";
    }

    private void SpeedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ApplyRateToPlayer();

    private void ApplyRateToPlayer()
    {
        _player?.SetRate(GetSelectedRate());
    }

    private float GetSelectedRate()
    {
        if (Playback.SpeedCombo.SelectedItem is ComboBoxItem item
            && item.Tag is string tag
            && float.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate)
            && rate > 0)
        {
            return rate;
        }

        return 1f;
    }

    private void FullscreenButton_Click(object sender, RoutedEventArgs e) => _ = ToggleFullScreenAsync();

    private void VideoPlayerPage_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && HostWindow?.IsFullScreen == true)
        {
            _ = ExitFullScreenAsync();
            e.Handled = true;
            return;
        }

        if (e.Key == Windows.System.VirtualKey.F)
        {
            _ = ToggleFullScreenAsync();
            e.Handled = true;
        }
    }

    private Task ToggleFullScreenAsync()
        => HostWindow?.IsFullScreen == true ? ExitFullScreenAsync() : EnterFullScreenAsync();

    private async Task EnterFullScreenAsync()
    {
        var window = HostWindow;
        if (window is null || _transitioning)
        {
            return;
        }

        _transitioning = true;
        ApplyFullScreenLayout();
        await window.SetFullScreenAsync(true);
        Playback.FullScreenIcon.Glyph = "\uE73F";
        _transitioning = false;
    }

    private async Task ExitFullScreenAsync()
    {
        if (_transitioning)
        {
            return;
        }

        _transitioning = true;
        if (HostWindow is not null)
        {
            await HostWindow.SetFullScreenAsync(false);
        }

        RestoreWindowedLayout();
        _transitioning = false;
    }

    private void ExitFullScreen()
    {
        HostWindow?.SetFullScreen(false);
        RestoreWindowedLayout();
    }

    private void AppWindow_Changed(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (_transitioning || !args.DidPresenterChange || sender.Presenter.Kind == AppWindowPresenterKind.FullScreen)
        {
            return;
        }

        RestoreWindowedLayout();
    }

    private void ApplyFullScreenLayout()
    {
        HeaderPanel.Opacity = 1;
        HeaderPanel.Visibility = Visibility.Collapsed;
        RootGrid.Padding = new Thickness(0);
        RootGrid.RowSpacing = 0;
    }

    private void RestoreWindowedLayout()
    {
        RootGrid.Padding = WindowedPadding;
        RootGrid.RowSpacing = 12;
        HeaderPanel.Opacity = 1;
        HeaderPanel.Visibility = Visibility.Visible;
        Playback.FullScreenIcon.Glyph = "\uE740";
    }

    private static MainWindow? HostWindow => App.MainAppWindow as MainWindow;

    private void OnSeekPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_hasValidDuration)
        {
            return;
        }

        _dragging = true;
    }

    private void OnSeekReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        if (_player is null || !_hasValidDuration || _durationMs <= 0)
        {
            return;
        }

        var time = (long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _durationMs);
        if (_editing)
        {
            time = Math.Clamp(time, _trimStartMs, _trimEndMs);
        }

        _player.Time = time;
        UpdateClockAndBar();
    }

    private void SeekSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingSlider || !_dragging || !_hasValidDuration)
        {
            return;
        }

        Playback.PositionText.Text = FormatMs((long)(Playback.SeekSlider.Value / Playback.SeekSlider.Maximum * _durationMs));
    }

    private void UpdateClockAndBar()
    {
        if (_player is null)
        {
            return;
        }

        ApplyDuration(_player.Length, "vlc-timer");
        TryResume();
        if (_editing && _player.IsPlaying && _trimEndMs > _trimStartMs && _player.Time >= _trimEndMs - 80)
        {
            _player.Time = _trimStartMs;
        }

        if (!_dragging)
        {
            Playback.PositionText.Text = FormatMs(_player.Time);
        }

        if (_dragging || !_hasValidDuration)
        {
            return;
        }

        Playback.DurationText.Text = FormatMs(_durationMs);
        _updatingSlider = true;
        Playback.SeekSlider.Value = Math.Clamp(_player.Time / (double)_durationMs * Playback.SeekSlider.Maximum, 0, Playback.SeekSlider.Maximum);
        _updatingSlider = false;
        UpdatePlayIcon();
        RememberPosition(force: false);
    }

    private void HookPlayer()
    {
        if (_player is null)
        {
            return;
        }

        _player.Playing += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayIcon);
        _player.Paused += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            RememberPosition(force: true);
            UpdatePlayIcon();
        });
        _player.Stopped += (_, _) => DispatcherQueue.TryEnqueue(UpdatePlayIcon);
        _player.EndReached += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            _ended = true;
            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                PlaybackProgress.Save(_filePath, 0, 1);
            }

            UpdatePlayIcon();
        });
    }

    private void TryResume()
    {
        if (!_resumePending || _player is null || !_hasValidDuration || _editing)
        {
            return;
        }

        _resumePending = false;
        if (string.IsNullOrWhiteSpace(_filePath) || _durationMs <= _resumeMs + 10_000)
        {
            if (!string.IsNullOrWhiteSpace(_filePath))
            {
                PlaybackProgress.Save(_filePath, 0, 1);
            }

            return;
        }

        _player.Time = _resumeMs;
        AddedText.Text = $"Resumed at {FormatMs(_resumeMs)}.";
    }

    private void RememberPosition(bool force)
    {
        if (_player is null || string.IsNullOrWhiteSpace(_filePath) || !_hasValidDuration || _editing)
        {
            return;
        }

        var time = Math.Max(_player.Time, 0);
        if (!force && (time < 5_000 || Math.Abs(time - _lastRememberedMs) < 5_000))
        {
            return;
        }

        PlaybackProgress.Save(_filePath, time, _durationMs);
        _lastRememberedMs = time;
    }

    private void ResetDuration()
    {
        _hasValidDuration = false;
        _durationMs = 0;
        _dragging = false;
        Playback.SeekSlider.IsEnabled = false;
        Playback.SeekSlider.Value = 0;
        EditTrimButton.IsEnabled = false;
        Playback.DurationText.Text = "--:--";
        Playback.PositionText.Text = "00:00";
        Playback.SetHoverDuration(0);
    }

    private void ApplyDuration(long durationMs, string source)
    {
        Debug.WriteLine($"{DateTime.Now:O} source={source} durationMs={durationMs} usable={IsUsableDuration(durationMs)} alreadyValid={_hasValidDuration}");

        if (_hasValidDuration || !IsUsableDuration(durationMs))
        {
            return;
        }

        _hasValidDuration = true;
        _durationMs = durationMs;
        Playback.SetHoverDuration(durationMs);
        Playback.DurationText.Text = FormatMs(durationMs);
        Playback.SeekSlider.IsEnabled = true;
        EditTrimButton.IsEnabled = true;
        if (_editing)
        {
            _trimEndMs = durationMs;
            UpdateTrimSummary();
        }
    }

    private static bool IsUsableDuration(long durationMs)
        => durationMs > 0 && durationMs <= MaxRealisticMs && durationMs < int.MaxValue;

    private void UpdatePlayIcon()
    {
        Playback.PlayIcon.Glyph = _player?.IsPlaying == true ? "\uE769" : "\uE768";
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Restore original?",
            Content = "This replaces the current video with the untouched copy.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        var path = _filePath;
        try
        {
            if (_editing)
            {
                LeaveTrimMode();
            }

            _player?.SetPause(true);
            ReleasePlayer();
            App.MediaLibrary.RestoreOriginal(path);
            ResetDuration();
            _pendingPath = path;
            CreatePlayer();
            PlayFile(path);
            AddedText.Text = "Original restored.";
        }
        catch (Exception ex)
        {
            if (_player is null)
            {
                CreatePlayer();
                PlayFile(path);
            }

            AddedText.Text = ex.Message;
        }
    }

    private async void Bookmark_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var time = Math.Max(0, _player.Time);
        var note = await AskBookmarkNoteAsync(string.Empty);
        if (note is null)
        {
            return;
        }

        if (!PlaybackBookmarks.Add(_filePath, time, note))
        {
            AddedText.Text = $"Already bookmarked near {FormatMs(time)}.";
            return;
        }

        AddedText.Text = string.IsNullOrWhiteSpace(note)
            ? $"Bookmarked {FormatMs(time)}."
            : $"Bookmarked {FormatMs(time)}: {note}";
        ShowBookmarks();
    }

    private async Task<string?> AskBookmarkNoteAsync(string current)
    {
        var box = new TextBox
        {
            Text = current,
            PlaceholderText = "Why this moment? Optional",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 120
        };
        var dialog = new ContentDialog
        {
            Title = "Bookmark note",
            Content = box,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary ? box.Text : null;
    }

    private void ShowBookmarks()
    {
        var keepOpen = BookmarkPanel.Visibility == Visibility.Visible;
        BookmarkList.Children.Clear();
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            BookmarkHost.Visibility = Visibility.Collapsed;
            BookmarkPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var marks = PlaybackBookmarks.Load(_filePath);
        if (marks.Count == 0)
        {
            BookmarkHost.Visibility = Visibility.Collapsed;
            BookmarkPanel.Visibility = Visibility.Collapsed;
            return;
        }

        BookmarkHost.Visibility = Visibility.Visible;
        BookmarkPanel.Visibility = keepOpen ? Visibility.Visible : Visibility.Collapsed;
        foreach (var mark in marks)
        {
            BookmarkList.Children.Add(CreateBookmarkRow(mark.TimeMs, mark.Note));
        }
    }

    private void BookmarkToggle_Click(object sender, RoutedEventArgs e)
    {
        BookmarkPanel.Visibility = BookmarkPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private UIElement CreateBookmarkRow(long time, string note)
    {
        var jump = new Button
        {
            Content = FormatMs(time),
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 72
        };
        jump.Click += (_, _) => JumpTo(time);
        var edit = new Button { Content = "Edit", Padding = new Thickness(8, 4, 8, 4) };
        edit.Click += async (_, _) =>
        {
            var updated = await AskBookmarkNoteAsync(note);
            if (updated is null || string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            PlaybackBookmarks.UpdateNote(_filePath, time, updated);
            ShowBookmarks();
        };
        var remove = new Button { Content = "Remove", Padding = new Thickness(8, 4, 8, 4) };
        remove.Click += async (_, _) =>
        {
            var dialog = new ContentDialog
            {
                Title = "Remove bookmark?",
                Content = string.IsNullOrWhiteSpace(note)
                    ? $"Remove the bookmark at {FormatMs(time)}?"
                    : $"Remove the bookmark at {FormatMs(time)} and its note?",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary || string.IsNullOrWhiteSpace(_filePath))
            {
                return;
            }

            PlaybackBookmarks.Remove(_filePath, time);
            ShowBookmarks();
        };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 6,
            Children = { edit, remove }
        };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(actions, 1);
        row.Children.Add(jump);
        row.Children.Add(actions);
        var body = new StackPanel { Spacing = 4 };
        body.Children.Add(row);
        if (!string.IsNullOrWhiteSpace(note))
        {
            const int previewLength = 80;
            var shortened = note.Length > previewLength;
            var preview = shortened ? note[..previewLength].TrimEnd() + "…" : note;
            var expanded = false;
            var text = new TextBlock
            {
                Text = preview,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                TextWrapping = TextWrapping.Wrap
            };
            var noteHost = new Border { Child = text, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 255, 255, 255)) };
            if (shortened)
            {
                noteHost.Tapped += (_, _) =>
                {
                    expanded = !expanded;
                    text.Text = expanded ? note : preview;
                };
            }

            body.Children.Add(noteHost);
        }

        return new Border
        {
            Padding = new Thickness(8),
            CornerRadius = new CornerRadius(8),
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(40, 255, 255, 255)),
            Child = body
        };
    }

    private void JumpTo(long timeMs)
    {
        if (_player is null)
        {
            return;
        }

        if (_editing)
        {
            timeMs = Math.Clamp(timeMs, _trimStartMs, Math.Max(_trimStartMs, _trimEndMs));
        }

        _player.Time = timeMs;
        UpdateClockAndBar();
    }

    private async void Snapshot_Click(object sender, RoutedEventArgs e)
    {
        if (_player is null || string.IsNullOrWhiteSpace(_filePath) || _grabbingFrame)
        {
            return;
        }

        _grabbingFrame = true;
        Playback.SnapshotButton.IsEnabled = false;
        var path = _filePath;
        var time = TimeSpan.FromMilliseconds(Math.Max(0, _player.Time));
        var name = $"{Path.GetFileNameWithoutExtension(path)} {FormatMs((long)time.TotalMilliseconds)}";
        try
        {
            RememberPosition(force: true);
            var png = await VideoFrameGrab.GrabPngAsync(path, time);
            using var buffer = new MemoryStream(png);
            using var image = System.Drawing.Image.FromStream(buffer, useEmbeddedColorManagement: false, validateImageData: false);
            var shot = new PendingScreenshot(png, image.Width, image.Height, name);
            var left = Frame.Navigate(typeof(CapturePage), shot);
            if (!left)
            {
                return;
            }

            if (App.MainAppWindow is MainWindow window)
            {
                window.SyncNavigationSelection();
            }
        }
        catch (Exception ex)
        {
            AddedText.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(Frame?.Content, this))
            {
                _grabbingFrame = false;
                Playback.SnapshotButton.IsEnabled = true;
            }
        }
    }

    private void EditVideo_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        var item = App.MediaLibrary.GetById(Path.GetRelativePath(App.MediaLibrary.LibraryRoot, _filePath).Replace('\\', '/'));
        if (item is null)
        {
            return;
        }

        Frame.Navigate(typeof(VideoEditorPage), item);
    }

    private void EditTrim_Click(object sender, RoutedEventArgs e)
    {
        if (!_hasValidDuration || _durationMs <= 0 || string.IsNullOrWhiteSpace(_filePath))
        {
            return;
        }

        _editing = true;
        _trimStartMs = 0;
        _trimEndMs = _durationMs;
        WatchActions.Visibility = Visibility.Collapsed;
        TrimActions.Visibility = Visibility.Visible;
        Playback.BeginTrim();
        UpdateTrimSummary();
        AddedText.Text = "Drag the white ends. Playback stays inside the selection.";
    }

    private void CancelTrim_Click(object sender, RoutedEventArgs e) => LeaveTrimMode();

    private async void SaveTrim_Click(object sender, RoutedEventArgs e)
    {
        if (_savingTrim || !_editing || string.IsNullOrWhiteSpace(_filePath) || _trimEndMs - _trimStartMs < 400)
        {
            TrimSummary.Text = "Keep at least half a second.";
            return;
        }

        _savingTrim = true;
        TrimProgress.IsActive = true;
        TrimSummary.Text = "Saving the trimmed video…";
        var path = _filePath;
        var temp = path + ".trimming.mp4";
        try
        {
            _player?.SetPause(true);
            ReleasePlayer();
            await VideoTrimmer.TrimAsync(path, temp, TimeSpan.FromMilliseconds(_trimStartMs), TimeSpan.FromMilliseconds(_trimEndMs));
            App.MediaLibrary.PreserveOriginal(path);
            File.Replace(temp, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
            LeaveTrimMode();
            ResetDuration();
            _pendingPath = path;
            CreatePlayer();
            PlayFile(path);
            AddedText.Text = "Trimmed. The original is kept with your other originals.";
            RestoreButton.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // The failed export can stay until the next trim.
                }
            }

            if (_player is null)
            {
                CreatePlayer();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    PlayFile(path);
                }
            }

            TrimSummary.Text = ex.Message;
        }
        finally
        {
            _savingTrim = false;
            TrimProgress.IsActive = false;
        }
    }

    private void LeaveTrimMode()
    {
        _editing = false;
        Playback.EndTrim();
        TrimActions.Visibility = Visibility.Collapsed;
        WatchActions.Visibility = Visibility.Visible;
    }

    private void ApplyTrimFromBar()
    {
        if (!_editing || _durationMs <= 0)
        {
            return;
        }

        _trimStartMs = (long)(Playback.TrimStart * _durationMs);
        _trimEndMs = (long)(Playback.TrimEnd * _durationMs);
        if (_player is not null && (_player.Time < _trimStartMs || _player.Time > _trimEndMs))
        {
            _player.Time = _trimStartMs;
        }

        UpdateTrimSummary();
        UpdateClockAndBar();
    }

    private void SeekToFraction(double fraction)
    {
        if (_player is null || _durationMs <= 0)
        {
            return;
        }

        var time = (long)(fraction * _durationMs);
        _player.Time = _editing ? Math.Clamp(time, _trimStartMs, _trimEndMs) : time;
        UpdateClockAndBar();
    }

    private void UpdateTrimSummary()
    {
        var keep = Math.Max(0, _trimEndMs - _trimStartMs);
        TrimSummary.Text = $"Keep {FormatMs(keep)}  ·  {FormatMs(_trimStartMs)} to {FormatMs(_trimEndMs)}";
    }

    private void ReleasePlayer()
    {
        if (_player is not null)
        {
            _player.LengthChanged -= Player_LengthChanged;
            _player.Stop();
            VideoView.MediaPlayer = null;
            _player.Dispose();
            _player = null;
        }

        _libVlc?.Dispose();
        _libVlc = null;
    }

    private void CreatePlayer()
    {
        if (_swapChainOptions is null)
        {
            return;
        }

        _libVlc = new LibVLC(enableDebugLogs: false, _swapChainOptions);
        _player = new VlcMediaPlayer(_libVlc);
        _player.LengthChanged += Player_LengthChanged;
        HookPlayer();
        VideoView.MediaPlayer = _player;
        ApplyVolumeToPlayer();
        ApplyRateToPlayer();
    }

    private static string FormatMs(long durationMs)
    {
        if (durationMs < 0 || durationMs > MaxRealisticMs)
        {
            return "00:00";
        }

        var totalSeconds = durationMs / 1000;
        var hours = totalSeconds / 3600;
        var minutes = totalSeconds % 3600 / 60;
        var seconds = totalSeconds % 60;
        return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
    }
}
