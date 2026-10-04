using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.Core.Library;
using Windows.System;

namespace PersonalMediaPlayer.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private bool _applying = true;

    public SettingsViewModel(IMediaLibrary library)
    {
        LibraryPath = library.LibraryRoot;
        ThemeIndex = (int)ThemeSettings.Load();
        includeCursor = CaptureSettings.LoadIncludeCursor();
        delayIndex = CaptureSettings.IndexFromSeconds(CaptureSettings.LoadDelaySeconds());
        var languages = StreamLanguageSettings.Load();
        audioIndex = StreamLanguageSettings.AudioIndex(languages.AudioLanguage);
        captionIndex = StreamLanguageSettings.CaptionIndex(languages.CaptionLanguage);
        AudioOptions = new[] { "Original" }.Concat(StreamLanguageSettings.Languages.Select(item => item.Name)).ToArray();
        CaptionOptions = new[] { "Off" }.Concat(StreamLanguageSettings.Languages.Select(item => item.Name)).ToArray();
        _applying = false;
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

    [ObservableProperty]
    private int audioIndex;

    [ObservableProperty]
    private int captionIndex;

    public IReadOnlyList<string> AudioOptions { get; }

    public IReadOnlyList<string> CaptionOptions { get; }

    public IReadOnlyList<string> DelayOptions { get; } =
    [
        "None",
        "3 seconds",
        "5 seconds",
        "10 seconds"
    ];

    partial void OnThemeIndexChanged(int value)
    {
        if (_applying)
        {
            return;
        }

        var theme = value switch
        {
            1 => AppTheme.Light,
            2 => AppTheme.Dark,
            _ => AppTheme.System
        };

        ThemeSettings.Save(theme);
        ThemeSettings.Apply(App.MainAppWindow, theme);
    }

    partial void OnIncludeCursorChanged(bool value)
    {
        if (!_applying)
        {
            CaptureSettings.SaveIncludeCursor(value);
        }
    }

    partial void OnDelayIndexChanged(int value)
    {
        if (_applying)
        {
            return;
        }

        var index = Math.Clamp(value, 0, CaptureSettings.DelayChoices.Length - 1);
        CaptureSettings.SaveDelaySeconds(CaptureSettings.DelayChoices[index]);
    }

    partial void OnAudioIndexChanged(int value)
    {
        if (!_applying)
        {
            SaveStreamLanguages();
        }
    }

    partial void OnCaptionIndexChanged(int value)
    {
        if (!_applying)
        {
            SaveStreamLanguages();
        }
    }

    public void ReloadSavedChoices()
    {
        _applying = true;
        ThemeIndex = (int)ThemeSettings.Load();
        IncludeCursor = CaptureSettings.LoadIncludeCursor();
        DelayIndex = CaptureSettings.IndexFromSeconds(CaptureSettings.LoadDelaySeconds());
        var languages = StreamLanguageSettings.Load();
        AudioIndex = StreamLanguageSettings.AudioIndex(languages.AudioLanguage);
        CaptionIndex = StreamLanguageSettings.CaptionIndex(languages.CaptionLanguage);
        _applying = false;
        ThemeSettings.Apply(App.MainAppWindow, ThemeSettings.Load());
    }

    private void SaveStreamLanguages()
    {
        StreamLanguageSettings.Save(new StreamLanguagePreference(
            StreamLanguageSettings.LanguageFromIndex(AudioIndex),
            StreamLanguageSettings.LanguageFromIndex(CaptionIndex)));
    }

    [RelayCommand]
    private async Task OpenLibraryFolderAsync()
    {
        Directory.CreateDirectory(LibraryPath);
        await Launcher.LaunchFolderPathAsync(LibraryPath);
    }
}
