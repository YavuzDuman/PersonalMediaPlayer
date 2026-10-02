using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class ShellPage : UserControl
{
    private StreamOpenRequest? _heldStream;

    public ShellPage()
    {
        InitializeComponent();
        NavigationHelper.ContentFrame = NavFrame;
        NavigationHelper.RequestCapture = OpenCapture;
        NavFrame.Navigated += NavFrame_Navigated;
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
        if (NavFrame.CurrentSourcePageType == pageType && !NavFrame.CanGoBack)
        {
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
        if (!TryLeaveFor(typeof(VideoPlayerPage), request))
        {
            return;
        }

        // The player is not a section, so Back returns to the page that was open.
        NavFrame.Navigate(typeof(VideoPlayerPage), request);
    }

    private void NavFrame_Navigated(object sender, NavigationEventArgs e)
    {
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
