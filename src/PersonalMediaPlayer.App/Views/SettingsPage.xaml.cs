using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.ViewModels;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.MediaLibrary);
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
