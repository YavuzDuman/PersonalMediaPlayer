using Microsoft.UI;
using Microsoft.UI.Xaml;

namespace PersonalMediaPlayer.App.Helpers;

internal enum AppTheme
{
    System,
    Light,
    Dark
}

internal static class ThemeSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PersonalMediaPlayer",
        "theme.txt");

    public static AppTheme Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return AppTheme.System;
            }

            return Enum.TryParse<AppTheme>(File.ReadAllText(FilePath).Trim(), ignoreCase: true, out var theme)
                ? theme
                : AppTheme.System;
        }
        catch
        {
            return AppTheme.System;
        }
    }

    public static void Save(AppTheme theme)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, theme.ToString());
    }

    public static void Apply(Window window, AppTheme theme)
    {
        var elementTheme = theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };

        if (window.Content is FrameworkElement root)
        {
            root.RequestedTheme = elementTheme;
        }

        ApplyTitleBar(window, elementTheme);
    }

    private static void ApplyTitleBar(Window window, ElementTheme theme)
    {
        if (!Microsoft.UI.Windowing.AppWindowTitleBar.IsCustomizationSupported())
        {
            return;
        }

        var titleBar = window.AppWindow.TitleBar;
        var dark = theme == ElementTheme.Dark
            || (theme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark);

        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonPressedForegroundColor = dark ? Colors.White : Colors.Black;
        titleBar.ButtonHoverBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(40, 255, 255, 255)
            : Windows.UI.Color.FromArgb(40, 0, 0, 0);
        titleBar.ButtonPressedBackgroundColor = dark
            ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
            : Windows.UI.Color.FromArgb(60, 0, 0, 0);
    }
}
