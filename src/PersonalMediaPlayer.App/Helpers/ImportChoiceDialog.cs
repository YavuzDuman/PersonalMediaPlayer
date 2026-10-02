using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace PersonalMediaPlayer.App.Helpers;

internal enum ImportChoice
{
    Copy,
    Link
}

internal static class ImportChoiceDialog
{
    public static async Task<ImportChoice?> AskAsync(XamlRoot root, int count)
    {
        var one = count == 1;
        var dialog = new ContentDialog
        {
            Title = one ? "Add this file" : "Add these files",
            Content = new TextBlock
            {
                Text = one
                    ? "Copy it into the library, or use it from where it is. A linked file stays in place. Removing it from the library does not delete the original."
                    : "Copy them into the library, or use them from where they are. A linked file stays in place. Removing them from the library does not delete the originals.",
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            },
            PrimaryButtonText = "Copy",
            SecondaryButtonText = "Use where it is",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => ImportChoice.Copy,
            ContentDialogResult.Secondary => ImportChoice.Link,
            _ => null
        };
    }
}
