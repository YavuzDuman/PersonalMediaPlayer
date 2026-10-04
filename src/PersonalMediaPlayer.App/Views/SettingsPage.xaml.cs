using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.App.Playback;
using PersonalMediaPlayer.App.Storage;
using PersonalMediaPlayer.App.ViewModels;

namespace PersonalMediaPlayer.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _backupBusy;

    public SettingsPage()
    {
        ViewModel = new SettingsViewModel(App.MediaLibrary);
        InitializeComponent();
        BackupStatus.Closed += (_, _) =>
        {
            if (!BackupStatus.IsOpen)
            {
                BackupStatus.Visibility = Visibility.Collapsed;
            }
        };
    }

    public SettingsViewModel ViewModel { get; }

    private async void ExportBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy)
        {
            return;
        }

        _backupBusy = true;
        try
        {
            var file = await FilePickerHelper.PickSaveBackupAsync(App.MainAppWindow);
            if (file is null)
            {
                return;
            }

            try
            {
                AppDataBackup.Export(AppDataBackup.DefaultRoot, file.Path);
                ShowBackupStatus("Backup saved.", InfoBarSeverity.Success);
            }
            catch (IOException)
            {
                ShowBackupStatus("Could not save the backup.", InfoBarSeverity.Error);
            }
            catch (UnauthorizedAccessException)
            {
                ShowBackupStatus("Could not save the backup.", InfoBarSeverity.Error);
            }
        }
        finally
        {
            _backupBusy = false;
        }
    }

    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (_backupBusy)
        {
            return;
        }

        _backupBusy = true;
        try
        {
            var file = await FilePickerHelper.PickBackupAsync(App.MainAppWindow);
            if (file is null)
            {
                return;
            }

            AppDataBackup.BackupPreview preview;
            try
            {
                preview = AppDataBackup.Read(file.Path);
            }
            catch (InvalidDataException ex)
            {
                ShowBackupStatus(string.IsNullOrWhiteSpace(ex.Message) ? "This backup file is not one this app can read." : ex.Message, InfoBarSeverity.Error);
                return;
            }
            catch (IOException)
            {
                ShowBackupStatus("Could not read that backup.", InfoBarSeverity.Error);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                ShowBackupStatus("Could not read that backup.", InfoBarSeverity.Error);
                return;
            }

            var dialog = new ContentDialog
            {
                Title = "Restore this backup?",
                Content = new ScrollViewer
                {
                    MaxHeight = 360,
                    Content = new TextBlock
                    {
                        Text = preview.Confirmation,
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                        MaxWidth = 440
                    }
                },
                PrimaryButtonText = "Restore",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            try
            {
                preview.Apply(AppDataBackup.DefaultRoot);
            }
            catch (IOException ex)
            {
                ShowBackupStatus(string.IsNullOrWhiteSpace(ex.Message) ? "Could not restore that backup." : ex.Message, InfoBarSeverity.Error);
                return;
            }
            catch (UnauthorizedAccessException)
            {
                ShowBackupStatus("Could not restore that backup.", InfoBarSeverity.Error);
                return;
            }

            PlaylistChanges.Dismiss();
            App.MediaLibrary.ReloadLinks();
            ViewModel.ReloadSavedChoices();
            ShowBackupStatus("Backup restored.", InfoBarSeverity.Success);
        }
        finally
        {
            _backupBusy = false;
        }
    }

    private void ShowBackupStatus(string message, InfoBarSeverity severity)
    {
        BackupStatus.Severity = severity;
        BackupStatus.Message = message;
        BackupStatus.Visibility = Visibility.Visible;
        BackupStatus.IsOpen = true;
    }
}
