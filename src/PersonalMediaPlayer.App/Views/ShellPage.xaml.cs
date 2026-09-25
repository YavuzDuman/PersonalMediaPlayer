using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class ShellPage : UserControl
{
    public ShellPage()
    {
        InitializeComponent();
        NavigationHelper.ContentFrame = NavFrame;
        NavigationHelper.RequestCapture = OpenCapture;
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
            if (item.Tag as string == "library")
            {
                NavView.SelectedItem = item;
                NavigateToSection(typeof(LibraryPage));
                break;
            }
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
            case "library":
                NavigateToSection(typeof(LibraryPage));
                break;
            case "recordings":
                NavigateToSection(typeof(RecordingsPage));
                break;
            case "capture":
                NavigateToSection(typeof(CapturePage));
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

        if (NavFrame.Content is RecordingsPage recordings && !recordings.PrepareToLeave(typeof(CapturePage), kind, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is CapturePage capture && !capture.PrepareToLeave(typeof(CapturePage), kind, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is VideoPlayerPage video && !video.PrepareToLeave(typeof(CapturePage), kind, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is VideoEditorPage editor && !editor.PrepareToLeave(typeof(CapturePage), kind, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is MediaPreviewPage preview && !preview.PrepareToLeave(typeof(CapturePage), kind, back: false))
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

        NavFrame.GoBack();
    }

    private void NavigateToSection(Type pageType)
    {
        if (NavFrame.CurrentSourcePageType == pageType && !NavFrame.CanGoBack)
        {
            return;
        }

        if (NavFrame.Content is RecordingsPage recordings && !recordings.PrepareToLeave(pageType, null, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is CapturePage capture && !capture.PrepareToLeave(pageType, null, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is VideoPlayerPage video && !video.PrepareToLeave(pageType, null, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is VideoEditorPage editor && !editor.PrepareToLeave(pageType, null, back: false))
        {
            SyncNavSelection();
            return;
        }

        if (NavFrame.Content is MediaPreviewPage preview && !preview.PrepareToLeave(pageType, null, back: false))
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

        var tag = type == typeof(CapturePage)
            ? "capture"
            : type == typeof(RecordingsPage)
                ? "recordings"
                : "library";
        SelectNavTag(tag);
    }
}
