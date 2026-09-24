using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Capture;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.ViewModels;

public sealed partial class CaptureViewModel : ObservableObject
{
    private readonly IMediaLibrary _library;
    private byte[]? _pngBytes;

    public CaptureViewModel(IMediaLibrary library)
    {
        _library = library;
        includeCursor = CaptureSettings.LoadIncludeCursor();
        delayIndex = CaptureSettings.IndexFromSeconds(CaptureSettings.LoadDelaySeconds());
    }

    [ObservableProperty]
    private MediaItem? item;

    [ObservableProperty]
    private BitmapImage? previewImage;

    [ObservableProperty]
    private string detailsExtra = string.Empty;

    [ObservableProperty]
    private string recognizedText = string.Empty;

    [ObservableProperty]
    private bool isReadingText;

    [ObservableProperty]
    private string statusMessage = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity statusSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool hasStatus;

    [ObservableProperty]
    private bool hasPreview;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool isSaved;

    [ObservableProperty]
    private string fileName = string.Empty;

    [ObservableProperty]
    private int captureModeIndex;

    [ObservableProperty]
    private bool includeCursor;

    [ObservableProperty]
    private int delayIndex;

    [ObservableProperty]
    private int pixelWidth;

    [ObservableProperty]
    private int pixelHeight;

    public IReadOnlyList<string> DelayOptions { get; } =
    [
        "None",
        "3 seconds",
        "5 seconds",
        "10 seconds"
    ];

    public IReadOnlyList<string> CaptureModes { get; } =
    [
        "Selected area",
        "Single window",
        "Single monitor",
        "All monitors",
        "Fullscreen",
        "Text file"
    ];

    internal CaptureKind Kind
        => (CaptureKind)Math.Clamp(CaptureModeIndex, 0, CaptureModes.Count - 1);

    public string ModeInstructions => Kind switch
    {
        CaptureKind.SingleWindow => "Click the window you want. Esc or Alt+F4 cancels. Save only if you want to keep it.",
        CaptureKind.SingleMonitor => "Click the monitor you want. Esc or Alt+F4 cancels. Save only if you want to keep it.",
        CaptureKind.AllMonitors => "Captures every display at once. Save only if you want to keep it.",
        CaptureKind.Fullscreen => "Captures the display under the pointer. Save only if you want to keep it.",
        CaptureKind.TextFile => "Pick a .txt file. The full text is drawn as one long image. Save only if you want to keep it.",
        _ => "Drag a region on any screen. Esc or Alt+F4 cancels. Save only if you want to keep it."
    };

    public bool IsScreenCapture => Kind != CaptureKind.TextFile;

    public int DelaySeconds
    {
        get
        {
            var index = Math.Clamp(DelayIndex, 0, CaptureSettings.DelayChoices.Length - 1);
            return CaptureSettings.DelayChoices[index];
        }
    }

    public string CaptureButtonLabel => Kind == CaptureKind.TextFile ? "Choose .txt" : "New screenshot";

    public bool CanCapture => !IsBusy;

    public bool CanRename => HasPreview && !IsSaved && !IsBusy;

    public bool CanSave => HasPreview && !IsSaved && !IsBusy && !string.IsNullOrWhiteSpace(FileName);

    public bool CanShare => HasPreview && IsSaved && Item is not null && !IsBusy;

    public bool CanCopy => HasPreview && !IsBusy;

    public bool CanReadText => HasPreview && !IsBusy && !IsReadingText;

    public bool CanCopyText => HasPreview && !IsReadingText && !string.IsNullOrWhiteSpace(RecognizedText);

    public bool HasUnsaved => HasPreview && !IsSaved;

    public string Title => IsSaved ? Item?.DisplayName ?? FileName : FileName;

    public string SizeAndTime
    {
        get
        {
            if (!HasPreview)
            {
                return string.Empty;
            }

            var size = IsSaved && Item is not null ? Item.SizeLabel : FormatSize(_pngBytes?.Length ?? 0);
            var when = IsSaved && Item is not null ? Item.ImportedAt.ToLocalTime().ToString("g") : "Unsaved";
            return $"{size} · {when}";
        }
    }

    public string FolderLabel => !HasPreview
        ? string.Empty
        : IsSaved
            ? $"Folders: {(string.IsNullOrWhiteSpace(Item?.FolderName) ? LibraryFolder.Screenshots : Item.FolderName)}"
            : "Not saved yet — nothing is in Screenshots until you save.";

    public string PathLabel => IsSaved ? Item?.FilePath ?? string.Empty : "Not saved yet";

    public string SaveHint => IsSaved
        ? "Saved as a PNG in Screenshots."
        : "Not saved. Click Save to store this PNG in Screenshots.";

    public string DiscardLabel => IsSaved ? "Delete" : "Discard";

    public byte[]? PngBytes => _pngBytes;

    public string NormalizedFileName
    {
        get
        {
            var name = FileName.Trim();
            foreach (var character in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(character, '_');
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException("Enter a file name before saving.");
            }

            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                name += ".png";
            }

            return name;
        }
    }

    public async Task ShowPendingAsync(byte[] pngBytes, int pixelWidth, int pixelHeight, string? suggestedName = null)
    {
        _pngBytes = pngBytes;
        Item = null;
        IsSaved = false;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        FileName = string.IsNullOrWhiteSpace(suggestedName)
            ? $"Screenshot {DateTime.Now:yyyy-MM-dd HH-mm-ss}"
            : suggestedName;
        HasPreview = true;
        RecognizedText = string.Empty;
        PreviewImage = await ImageLoader.LoadAsync(pngBytes);
        DetailsExtra = $"Resolution: {pixelWidth} × {pixelHeight}";
        NotifyPreview();
        ShowStatus("Review and mark up the screenshot, then Save to keep it in Screenshots.", InfoBarSeverity.Informational);
    }

    public async Task ReplaceImageAsync(byte[] pngBytes, int pixelWidth, int pixelHeight)
    {
        _pngBytes = pngBytes;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        RecognizedText = string.Empty;
        PreviewImage = await ImageLoader.LoadAsync(pngBytes);
        DetailsExtra = $"Resolution: {pixelWidth} × {pixelHeight}";
        NotifyPreview();
    }

    public async Task MarkSavedAsync(MediaItem mediaItem)
    {
        Item = mediaItem;
        IsSaved = true;
        FileName = Path.GetFileNameWithoutExtension(mediaItem.DisplayName);
        DetailsExtra = await MediaDetails.DescribeAsync(mediaItem);
        NotifyPreview();
        ShowStatus($"Saved to Screenshots: {mediaItem.DisplayName}", InfoBarSeverity.Success);
    }

    public async Task RefreshAsync()
    {
        if (Item is null)
        {
            return;
        }

        var latest = _library.GetById(Item.Id);
        if (latest is null)
        {
            Clear();
            return;
        }

        Item = latest;
        DetailsExtra = await MediaDetails.DescribeAsync(latest);
        NotifyPreview();
    }

    public void Clear()
    {
        _pngBytes = null;
        Item = null;
        PreviewImage = null;
        DetailsExtra = string.Empty;
        RecognizedText = string.Empty;
        FileName = string.Empty;
        PixelWidth = 0;
        PixelHeight = 0;
        IsSaved = false;
        HasPreview = false;
        NotifyPreview();
    }

    partial void OnIsBusyChanged(bool value) => NotifyPreview();

    partial void OnIsReadingTextChanged(bool value) => NotifyPreview();

    partial void OnRecognizedTextChanged(string value) => NotifyPreview();

    partial void OnHasPreviewChanged(bool value) => NotifyPreview();

    partial void OnFileNameChanged(string value) => NotifyPreview();

    partial void OnIncludeCursorChanged(bool value) => CaptureSettings.SaveIncludeCursor(value);

    partial void OnDelayIndexChanged(int value)
    {
        var index = Math.Clamp(value, 0, CaptureSettings.DelayChoices.Length - 1);
        CaptureSettings.SaveDelaySeconds(CaptureSettings.DelayChoices[index]);
        OnPropertyChanged(nameof(DelaySeconds));
    }

    partial void OnCaptureModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(ModeInstructions));
        OnPropertyChanged(nameof(IsScreenCapture));
        OnPropertyChanged(nameof(CaptureButtonLabel));
    }

    public void ShowStatus(string message, InfoBarSeverity severity)
    {
        HasStatus = false;
        StatusMessage = message;
        StatusSeverity = severity;
        HasStatus = !string.IsNullOrWhiteSpace(message);
    }

    private void NotifyPreview()
    {
        OnPropertyChanged(nameof(CanCapture));
        OnPropertyChanged(nameof(CanRename));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanShare));
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(CanReadText));
        OnPropertyChanged(nameof(CanCopyText));
        OnPropertyChanged(nameof(HasUnsaved));
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SizeAndTime));
        OnPropertyChanged(nameof(FolderLabel));
        OnPropertyChanged(nameof(PathLabel));
        OnPropertyChanged(nameof(SaveHint));
        OnPropertyChanged(nameof(DiscardLabel));
    }

    private static string FormatSize(int bytes)
    {
        const double kb = 1024;
        const double mb = kb * 1024;
        if (bytes >= mb)
        {
            return $"{bytes / mb:0.0} MB";
        }

        if (bytes >= kb)
        {
            return $"{bytes / kb:0} KB";
        }

        return $"{bytes} B";
    }
}
