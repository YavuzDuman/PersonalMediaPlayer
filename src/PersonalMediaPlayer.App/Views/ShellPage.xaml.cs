using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Subtitles;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class ShellPage : UserControl
{
    private enum PlayerSlot
    {
        Hidden,
        Full,
        Mini
    }

    public static readonly DependencyProperty ShowBackButtonProperty = DependencyProperty.Register(
        nameof(ShowBackButton),
        typeof(bool),
        typeof(ShellPage),
        new PropertyMetadata(false));

    private StreamOpenRequest? _heldStream;
    private VideoPlayerPage? _playerPage;
    private PlayerSlot _playerSlot = PlayerSlot.Hidden;

    public ShellPage()
    {
        InitializeComponent();
        NavigationHelper.ContentFrame = NavFrame;
        NavigationHelper.RequestCapture = OpenCapture;
        NavFrame.Navigated += NavFrame_Navigated;
    }

    public bool ShowBackButton
    {
        get => (bool)GetValue(ShowBackButtonProperty);
        set => SetValue(ShowBackButtonProperty, value);
    }

    public Frame ContentFrame => NavFrame;

    public bool IsPaneOpen
    {
        get => NavView.IsPaneOpen;
        set => NavView.IsPaneOpen = value;
    }

    public void TogglePane()
    {
        if (!NavView.IsPaneVisible || NavView.CompactPaneLength < 48)
        {
            EnsurePaneAvailable();
            NavView.IsPaneOpen = true;
            return;
        }

        NavView.IsPaneOpen = !NavView.IsPaneOpen;
    }

    public void SetPaneVisible(bool visible)
    {
        if (visible)
        {
            EnsurePaneAvailable();
            return;
        }

        NavView.IsPaneOpen = false;
        NavView.IsSettingsVisible = false;
        NavView.IsPaneVisible = false;
    }

    public void EnsurePaneAvailable()
    {
        NavView.IsPaneVisible = true;
        NavView.IsSettingsVisible = true;
        NameSettingsInEnglish();
        NavView.OpenPaneLength = 240;
        NavView.CompactPaneLength = 48;
        if (NavView.PaneDisplayMode == NavigationViewPaneDisplayMode.LeftMinimal)
        {
            NavView.PaneDisplayMode = NavigationViewPaneDisplayMode.Auto;
        }
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == "home")
            {
                NavView.SelectedItem = item;
                if (NavFrame.Content is null)
                {
                    NavigateToSection(typeof(HomePage));
                }

                break;
            }
        }

        NameSettingsInEnglish();
    }

    private void NameSettingsInEnglish()
    {
        if (NavView.SettingsItem is NavigationViewItem settings)
        {
            settings.Content = "Settings";
        }
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.IsSettingsInvoked)
        {
            NavigateToSection(typeof(SettingsPage));
            return;
        }

        if (args.InvokedItemContainer is not NavigationViewItem item)
        {
            return;
        }

        switch (item.Tag as string)
        {
            case "home":
                NavigateToSection(typeof(HomePage));
                break;
            case "library":
                NavigateToSection(typeof(LibraryPage));
                break;
            case "playlists":
                NavigateToSection(typeof(PlaylistsPage));
                break;
            case "words":
                NavigateToSection(typeof(SavedWordsPage));
                break;
            case "recordings":
                NavigateToSection(typeof(RecordingsPage));
                break;
            case "capture":
                NavigateToSection(typeof(CapturePage));
                break;
            case "download":
                NavigateToSection(typeof(DownloadPage));
                break;
            case "merge":
                NavigateToSection(typeof(MergePage));
                break;
        }
    }

    internal void OpenCapture(CaptureKind kind)
    {
        if (_playerSlot == PlayerSlot.Full)
        {
            if (_playerPage is not null && !_playerPage.PrepareToLeave(typeof(CapturePage), kind, back: false))
            {
                SyncNavSelection();
                return;
            }

            DockPlayer();
        }

        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == "capture")
            {
                NavView.SelectedItem = item;
                break;
            }
        }

        if (NavFrame.Content is CapturePage page)
        {
            SelectNavTag("capture");
            page.RequestCapture(kind);
            return;
        }

        if (!TryLeaveFor(typeof(CapturePage), kind))
        {
            SyncNavSelection();
            return;
        }

        if (!NavFrame.Navigate(typeof(CapturePage), kind))
        {
            SyncNavSelection();
            return;
        }

        SelectNavTag("capture");
        NavFrame.BackStack.Clear();
    }

    public void GoBack()
    {
        if (_playerSlot == PlayerSlot.Full)
        {
            if (_playerPage is not null && !_playerPage.PrepareToLeave(null, null, back: true))
            {
                return;
            }

            DockPlayer();
            return;
        }

        if (!NavFrame.CanGoBack)
        {
            return;
        }

        if (NavFrame.Content is RecordingsPage recordings && !recordings.PrepareToLeave(null, null, back: true))
        {
            return;
        }

        if (NavFrame.Content is CapturePage capture && !capture.PrepareToLeave(null, null, back: true))
        {
            return;
        }

        if (NavFrame.Content is VideoPlayerPage video && !video.PrepareToLeave(null, null, back: true))
        {
            return;
        }

        if (NavFrame.Content is VideoEditorPage editor && !editor.PrepareToLeave(null, null, back: true))
        {
            return;
        }

        if (NavFrame.Content is MediaPreviewPage preview && !preview.PrepareToLeave(null, null, back: true))
        {
            return;
        }

        if (NavFrame.Content is MergePage merge && !merge.PrepareToLeave(null, null, back: true))
        {
            return;
        }

        NavFrame.GoBack();
    }

    private void NavigateToSection(Type pageType)
    {
        if (_playerSlot == PlayerSlot.Full)
        {
            if (_playerPage is not null && !_playerPage.PrepareToLeave(pageType, null, back: false))
            {
                SyncNavSelection();
                return;
            }

            DockPlayer();
        }

        if (NavFrame.CurrentSourcePageType == pageType && !NavFrame.CanGoBack)
        {
            RefreshBack();
            return;
        }

        if (!TryLeaveFor(pageType, null))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Navigate(pageType))
        {
            NavFrame.BackStack.Clear();
            RefreshBack();
            return;
        }

        SyncNavSelection();
    }

    private bool TryLeaveFor(Type pageType, object? parameter)
    {
        if (NavFrame.Content is RecordingsPage recordings && !recordings.PrepareToLeave(pageType, parameter, back: false))
        {
            return false;
        }

        if (NavFrame.Content is CapturePage capture && !capture.PrepareToLeave(pageType, parameter, back: false))
        {
            return false;
        }

        if (NavFrame.Content is VideoPlayerPage video && !video.PrepareToLeave(pageType, parameter, back: false))
        {
            return false;
        }

        if (NavFrame.Content is VideoEditorPage editor && !editor.PrepareToLeave(pageType, parameter, back: false))
        {
            return false;
        }

        if (NavFrame.Content is MediaPreviewPage preview && !preview.PrepareToLeave(pageType, parameter, back: false))
        {
            return false;
        }

        if (NavFrame.Content is MergePage merge && !merge.PrepareToLeave(pageType, parameter, back: false))
        {
            return false;
        }

        return true;
    }

    internal void OpenStream(StreamOpenRequest request)
    {
        _heldStream = request;
        if (_playerSlot == PlayerSlot.Full && _playerPage is not null && !_playerPage.PrepareToLeave(typeof(VideoPlayerPage), request, back: false))
        {
            return;
        }

        if (!TryLeaveFor(typeof(VideoPlayerPage), request))
        {
            return;
        }

        _heldStream = null;
        ShowPlayer(request);
    }

    internal bool ShowPlayer(object? parameter)
    {
        if (parameter is null)
        {
            return false;
        }

        if (_playerSlot == PlayerSlot.Full && _playerPage is not null && !_playerPage.PrepareToLeave(typeof(VideoPlayerPage), parameter, back: false))
        {
            return false;
        }

        if (!TryLeaveFor(typeof(VideoPlayerPage), parameter))
        {
            return false;
        }

        if (parameter is StreamOpenRequest)
        {
            _heldStream = null;
        }

        Present(PlayerSlot.Full);
        PlayerPage.Open(parameter);
        PlayerPage.Focus(FocusState.Programmatic);
        RefreshBack();
        return true;
    }

    internal void ExpandPlayer()
    {
        if (_playerPage is null || !_playerPage.HasSession || _playerSlot == PlayerSlot.Hidden)
        {
            return;
        }

        Present(PlayerSlot.Full);
        _playerPage.Focus(FocusState.Programmatic);
        RefreshBack();
    }

    internal bool TryKeepPlayingPage(Uri page)
    {
        if (_playerPage is null || !_playerPage.HasSession || !_playerPage.MatchesPage(page))
        {
            return false;
        }

        Present(PlayerSlot.Full);
        _playerPage.Focus(FocusState.Programmatic);
        RefreshBack();
        return true;
    }

    internal bool TryFocusPlayingPage(Uri page, SavedWord word)
    {
        if (_playerPage is null || !_playerPage.HasSession || !_playerPage.MatchesPage(page))
        {
            return false;
        }

        var name = string.IsNullOrWhiteSpace(word.SourceName) ? page.Host : word.SourceName.Trim();
        Present(PlayerSlot.Full);
        _playerPage.Open(new StreamOpenRequest(page, name, Page: page, StartMs: word.TimeMs, Focus: word));
        _playerPage.Focus(FocusState.Programmatic);
        RefreshBack();
        return true;
    }

    internal void DockPlayer()
    {
        if (_playerSlot == PlayerSlot.Hidden)
        {
            return;
        }

        if (_playerPage is null || !_playerPage.HasSession)
        {
            ClosePlayer();
            return;
        }

        _playerPage.LeaveFullScreen();
        Present(PlayerSlot.Mini);
        RefreshBack();
    }

    internal void ClosePlayer()
    {
        _playerPage?.Shutdown();
        Present(PlayerSlot.Hidden);
        RefreshBack();
    }

    internal void RememberPlayer() => _playerPage?.RememberForClose();

    internal bool LeavePlayerFor(Type pageType, object? parameter, string? navTag)
    {
        if (_playerSlot == PlayerSlot.Full)
        {
            DockPlayer();
        }

        if (!TryLeaveFor(pageType, parameter))
        {
            return false;
        }

        if (!NavFrame.Navigate(pageType, parameter))
        {
            return false;
        }

        if (navTag is not null)
        {
            SelectNavTag(navTag);
        }

        RefreshBack();
        return true;
    }

    internal void CompletePlayerLeave(bool back, Type? pageType, object? parameter)
    {
        if (pageType == typeof(VideoPlayerPage) && parameter is not null)
        {
            ShowPlayer(parameter);
            return;
        }

        DockPlayer();
        if (back || pageType is null)
        {
            RefreshBack();
            return;
        }

        if (NavFrame.Navigate(pageType, parameter))
        {
            NavFrame.BackStack.Clear();
            SyncNavSelection();
        }

        RefreshBack();
    }

    private VideoPlayerPage PlayerPage
    {
        get
        {
            if (_playerPage is null)
            {
                _playerPage = new VideoPlayerPage();
                PlayerHost.Child = _playerPage;
            }

            return _playerPage;
        }
    }

    private void Present(PlayerSlot slot)
    {
        _playerSlot = slot;
        if (slot == PlayerSlot.Hidden)
        {
            NavFrame.Visibility = Visibility.Visible;
            PlayerHost.Visibility = Visibility.Collapsed;
            return;
        }

        var mini = slot == PlayerSlot.Mini;
        // Hide the page underneath. A theme brush stays see-through over Mica, so the
        // video name was drawn on top of that page.
        NavFrame.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        PlayerHost.Visibility = Visibility.Visible;
        PlayerHost.HorizontalAlignment = mini ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        PlayerHost.VerticalAlignment = mini ? VerticalAlignment.Bottom : VerticalAlignment.Stretch;
        PlayerHost.Width = mini ? 360 : double.NaN;
        PlayerHost.Height = mini ? 320 : double.NaN;
        PlayerHost.Margin = mini ? new Thickness(16) : new Thickness(0);
        PlayerHost.BorderThickness = mini ? new Thickness(1) : new Thickness(0);
        PlayerHost.CornerRadius = mini ? new CornerRadius(16) : new CornerRadius(0);
        var dark = ActualTheme == ElementTheme.Dark;
        PlayerHost.Background = new SolidColorBrush(mini
            ? (dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White)
            : (dark
                ? Windows.UI.Color.FromArgb(255, 32, 32, 32)
                : Windows.UI.Color.FromArgb(255, 243, 243, 243)));
        PlayerHost.BorderBrush = new SolidColorBrush(dark
            ? Windows.UI.Color.FromArgb(90, 255, 255, 255)
            : Windows.UI.Color.FromArgb(70, 0, 0, 0));
        if (!mini)
        {
            PlaybackFocus.PauseOthers(_playerPage);
        }

        PlayerPage.SetChrome(mini);
        PlayerPage.ApplyBackdrop(mini);
    }

    private void RefreshBack()
        => ShowBackButton = _playerSlot == PlayerSlot.Full || NavFrame.CanGoBack;

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
        RefreshBack();
        // Stay keeps the address. A later leave can replace the page's pending destination.
        if (_heldStream is not StreamOpenRequest held)
        {
            return;
        }

        if (e.Parameter is StreamOpenRequest)
        {
            _heldStream = null;
            return;
        }

        _heldStream = null;
        DispatcherQueue.TryEnqueue(() => OpenStream(held));
    }

    private void SelectNavTag(string tag)
    {
        foreach (var item in NavView.MenuItems.OfType<NavigationViewItem>())
        {
            if (item.Tag as string == tag)
            {
                NavView.SelectedItem = item;
                return;
            }
        }
    }

    internal void SyncNavSelection()
    {
        var type = NavFrame.CurrentSourcePageType;
        if (type == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
            return;
        }

        var tag = type == typeof(HomePage)
            ? "home"
            : type == typeof(PlaylistsPage)
            ? "playlists"
            : type == typeof(SavedWordsPage)
            ? "words"
            : type == typeof(CapturePage)
                ? "capture"
                : type == typeof(RecordingsPage)
                    ? "recordings"
                    : type == typeof(DownloadPage)
                        ? "download"
                        : type == typeof(MergePage)
                            ? "merge"
                            : "library";
        SelectNavTag(tag);
    }
}
