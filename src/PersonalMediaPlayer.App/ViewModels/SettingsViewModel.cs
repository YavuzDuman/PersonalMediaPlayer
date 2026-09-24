using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.Core.Library;
using Windows.System;

namespace PersonalMediaPlayer.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    public SettingsViewModel(IMediaLibrary library)
    {
        LibraryPath = library.LibraryRoot;
        ThemeIndex = (int)ThemeSettings.Load();
        includeCursor = CaptureSettings.LoadIncludeCursor();
        delayIndex = CaptureSettings.IndexFromSeconds(CaptureSettings.LoadDelaySeconds());
    }

    public string LibraryPath { get; }

    public IReadOnlyList<string> ThemeOptions { get; } =
    [
        "Use Windows setting",
        "Light",
        "Dark"
    ];

    [ObservableProperty]
    private int themeIndex;

    [ObservableProperty]
    private bool includeCursor;

    [ObservableProperty]
    private int delayIndex;

    public IReadOnlyList<string> DelayOptions { get; } =
    [
        "None",
        "3 seconds",
        "5 seconds",
        "10 seconds"
    ];

    partial void OnThemeIndexChanged(int value)
    {
        var theme = value switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System
        };

        ThemeSettings.Save(theme);
        ThemeSettings.Apply(App.MainAppWindow, theme);
    }

    partial void OnIncludeCursorChanged(bool value) => CaptureSettings.SaveIncludeCursor(value);

    partial void OnDelayIndexChanged(int value)
    {
        var index = Math.Clamp(value, 0, CaptureSettings.DelayChoices.Length - 1);
        CaptureSettings.SaveDelaySeconds(CaptureSettings.DelayChoices[index]);
    }

    [RelayCommand]
    private async Task OpenLibraryFolderAsync()
    {
        Directory.CreateDirectory(LibraryPath);
        await Launcher.LaunchFolderPathAsync(LibraryPath);
    }
}
