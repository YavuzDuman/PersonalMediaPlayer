using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;

namespace PersonalMediaPlayer.App.Download;

public sealed class DownloadQueueItem : INotifyPropertyChanged
{
    private string _status = "Queued";
    private double _progress;
    private string? _error;
    private string? _filePath;

    public DownloadQueueItem(string title, string url, DownloadQuality quality)
    {
        Title = title;
        Url = url;
        Quality = quality;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title { get; }

    public string Url { get; }

    public DownloadQuality Quality { get; }

    public string Status
    {
        get => _status;
        private set
        {
            if (_status == value)
            {
                return;
            }

            _status = value;
            Notify(nameof(Status));
            Notify(nameof(Detail));
            Notify(nameof(CanCancel));
            Notify(nameof(CanRetry));
            Notify(nameof(CanPreview));
            Notify(nameof(ShowProgress));
            Notify(nameof(CancelVisibility));
            Notify(nameof(RetryVisibility));
            Notify(nameof(PreviewVisibility));
            Notify(nameof(PauseVisibility));
            Notify(nameof(ResumeVisibility));
            Notify(nameof(ProgressVisibility));
        }
    }

    public double Progress
    {
        get => _progress;
        private set
        {
            _progress = value;
            Notify(nameof(Progress));
            Notify(nameof(Detail));
        }
    }

    public string? Error
    {
        get => _error;
        private set
        {
            _error = value;
            Notify(nameof(Error));
            Notify(nameof(Detail));
        }
    }

    public string? FilePath
    {
        get => _filePath;
        private set => _filePath = value;
    }

    public string? OutputPath { get; set; }

    public string Detail => Status switch
    {
        "Downloading" => $"Downloading {Progress:0}%",
        "Paused" => $"Paused · {Progress:0}%",
        "Failed" => Error ?? "Failed",
        _ => $"{Status} · {Quality.Label}"
    };

    public bool CanCancel => Status is "Queued" or "Downloading" or "Paused";

    public bool CanRetry => Status is "Failed" or "Cancelled";

    public bool CanPause => Status == "Downloading";

    public bool CanResume => Status == "Paused";

    public bool CanPreview => Status is "Ready";

    public bool ShowProgress => Status is "Downloading" or "Paused";

    public Visibility CancelVisibility => CanCancel ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RetryVisibility => CanRetry ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PreviewVisibility => CanPreview ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProgressVisibility => ShowProgress ? Visibility.Visible : Visibility.Collapsed;

    public Visibility PauseVisibility => CanPause ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ResumeVisibility => CanResume ? Visibility.Visible : Visibility.Collapsed;

    public void MarkQueued(bool keepProgress = false)
    {
        Error = null;
        if (!keepProgress)
        {
            Progress = 0;
        }

        Status = "Queued";
    }

    public void MarkDownloading(bool keepProgress = false)
    {
        Error = null;
        if (!keepProgress)
        {
            Progress = 0;
        }

        Status = "Downloading";
    }

    public void MarkPaused() => Status = "Paused";

    public void Restore(string status, double progress, string? filePath, string? outputPath)
    {
        _status = status;
        _progress = progress;
        _filePath = filePath;
        OutputPath = outputPath;
    }

    public void Report(double progress) => Progress = progress;

    public void MarkReady(string path)
    {
        FilePath = path;
        Progress = 100;
        Status = "Ready";
    }

    public void MarkFailed(string message)
    {
        Error = message;
        Status = "Failed";
    }

    public void MarkCancelled() => Status = "Cancelled";

    public void MarkSaved() => Status = "Saved";

    private void Notify(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
