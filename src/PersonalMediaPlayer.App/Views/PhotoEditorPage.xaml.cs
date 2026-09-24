using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.ViewModels;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class PhotoEditorPage : Page
{
    private bool _allowLeave;
    private Type? _pendingPageType;
    private object? _pendingParameter;
    private bool _pendingIsBack;

    public PhotoEditorPage()
    {
        ViewModel = new PhotoEditorViewModel(App.MediaLibrary);
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public PhotoEditorViewModel ViewModel { get; }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        if (e.Parameter is MediaItem item)
        {
            await ViewModel.LoadAsync(item);
        }
        else if (e.Parameter is string id)
        {
            var loaded = App.MediaLibrary.GetById(id);
            if (loaded is not null)
            {
                await ViewModel.LoadAsync(loaded);
            }
        }
    }

    protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
    {
        if (_allowLeave || !ViewModel.HasChanges)
        {
            return;
        }

        e.Cancel = true;
        _pendingPageType = e.SourcePageType;
        _pendingParameter = e.Parameter;
        _pendingIsBack = e.NavigationMode == NavigationMode.Back;
        _ = ConfirmPendingNavigationAsync();
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SourceItem is null || ViewModel.IsBusy)
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Save edited photo",
            Content = "Save as a new image, or overwrite the current one? The original is kept either way.",
            PrimaryButtonText = "Save as new",
            SecondaryButtonText = "Overwrite",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result is ContentDialogResult.None)
        {
            return;
        }

        try
        {
            var saved = await ViewModel.SaveAsync(overwrite: result == ContentDialogResult.Secondary);
            _allowLeave = true;
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }

            Frame.Navigate(typeof(MediaPreviewPage), saved);
        }
        catch
        {
            // ErrorMessage is already set on the view model.
        }
    }

    private async void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.HasChanges && !await ConfirmDiscardAsync())
        {
            return;
        }

        Leave(isBack: true, pageType: null, parameter: null);
    }

    private async Task ConfirmPendingNavigationAsync()
    {
        if (!await ConfirmDiscardAsync())
        {
            return;
        }

        Leave(_pendingIsBack, _pendingPageType, _pendingParameter);
    }

    private void Leave(bool isBack, Type? pageType, object? parameter)
    {
        _allowLeave = true;
        if (isBack && Frame.CanGoBack)
        {
            Frame.GoBack();
            return;
        }

        if (pageType is not null)
        {
            Frame.Navigate(pageType, parameter);
        }
        else if (Frame.CanGoBack)
        {
            Frame.GoBack();
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        var dialog = new ContentDialog
        {
            Title = "Discard edits?",
            Content = "The original photo will be kept. Your current edits will be lost.",
            PrimaryButtonText = "Discard",
            CloseButtonText = "Keep editing",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private void PreviewScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (PreviewScroll.ViewportWidth <= 0 || PreviewScroll.ViewportHeight <= 0)
        {
            return;
        }

        PreviewImage.Width = PreviewScroll.ViewportWidth;
        PreviewImage.Height = PreviewScroll.ViewportHeight;
    }

    private void PreviewScroll_Tapped(object sender, TappedRoutedEventArgs e)
    {
        ViewModel.Zoom = ClickZoom.HandleClick(PreviewScroll, e.GetPosition(PreviewScroll));
    }

    private void PreviewScroll_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (!ClickZoom.ControlIsDown())
        {
            return;
        }

        e.Handled = true;
        var point = e.GetCurrentPoint(PreviewScroll);
        ViewModel.Zoom = ClickZoom.HandleWheel(PreviewScroll, point.Position, point.Properties.MouseWheelDelta);
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.Zoom) && Math.Abs(ViewModel.Zoom - 1) < 0.01)
        {
            PreviewScroll.ChangeView(null, null, 1f, disableAnimation: true);
        }
    }
}
