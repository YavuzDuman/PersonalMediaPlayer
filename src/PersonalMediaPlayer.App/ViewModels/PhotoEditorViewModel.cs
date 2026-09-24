using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using PersonalMediaPlayer.App.Editing;
using PersonalMediaPlayer.App.Helpers;
using PersonalMediaPlayer.Core.Library;
using PersonalMediaPlayer.Core.Models;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.ViewModels;

public sealed partial class PhotoEditorViewModel : ObservableObject
{
    private readonly IMediaLibrary _library;
    private bool _updatingSize;

    public PhotoEditorViewModel(IMediaLibrary library)
    {
        _library = library;
    }

    public MediaItem? SourceItem { get; private set; }

    public string OriginalExtension { get; private set; } = ".png";

    public int OriginalWidth { get; private set; }

    public int OriginalHeight { get; private set; }

    public string OriginalName { get; private set; } = string.Empty;

    [ObservableProperty]
    private BitmapImage? previewImage;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private double width = 1;

    [ObservableProperty]
    private double height = 1;

    [ObservableProperty]
    private bool keepAspectRatio = true;

    [ObservableProperty]
    private double rotation;

    [ObservableProperty]
    private double zoom = 1;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? errorMessage;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string ZoomLabel => $"{Zoom * 100:0}%";

    public string RotationLabel => $"{Rotation:0}°";

    public string SizeHint => OriginalWidth == 0
        ? string.Empty
        : $"Original {OriginalWidth} × {OriginalHeight}";

    public double PreviewScaleX => OriginalWidth <= 0 ? 1 : Math.Max(0.01, Width / OriginalWidth);

    public double PreviewScaleY => OriginalHeight <= 0 ? 1 : Math.Max(0.01, Height / OriginalHeight);

    public bool HasChanges
    {
        get
        {
            if (SourceItem is null)
            {
                return false;
            }

            return !string.Equals(Name.Trim(), OriginalName, StringComparison.Ordinal)
                || (int)Math.Round(Width) != OriginalWidth
                || (int)Math.Round(Height) != OriginalHeight
                || Math.Abs(NormalizeRotation(Rotation)) > 0.05;
        }
    }

    public async Task LoadAsync(MediaItem item)
    {
        SourceItem = item;
        OriginalExtension = Path.GetExtension(item.FilePath);
        OriginalName = Path.GetFileNameWithoutExtension(item.DisplayName);
        Name = OriginalName;

        _library.PreserveOriginal(item.FilePath);

        var (pixelWidth, pixelHeight) = await ReadPixelSizeAsync(item.FilePath);
        OriginalWidth = pixelWidth;
        OriginalHeight = pixelHeight;

        _updatingSize = true;
        Width = pixelWidth;
        Height = pixelHeight;
        _updatingSize = false;

        Rotation = 0;
        Zoom = 1;
        ErrorMessage = null;
        PreviewImage = await ImageLoader.LoadAsync(item.FilePath);
        NotifyPreview();
    }

    [RelayCommand]
    private void RotateLeft() => Rotation = NormalizeRotation(Rotation - 90);

    [RelayCommand]
    private void RotateRight() => Rotation = NormalizeRotation(Rotation + 90);

    [RelayCommand]
    private void ResetRotation() => Rotation = 0;

    [RelayCommand]
    private void ResetEdits()
    {
        if (SourceItem is null)
        {
            return;
        }

        Name = OriginalName;
        KeepAspectRatio = true;
        _updatingSize = true;
        Width = OriginalWidth;
        Height = OriginalHeight;
        _updatingSize = false;
        Rotation = 0;
        Zoom = 1;
        ErrorMessage = null;
        NotifyPreview();
    }

    public async Task<MediaItem> SaveAsync(bool overwrite)
    {
        if (SourceItem is null)
        {
            throw new InvalidOperationException("No photo is loaded.");
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var outputWidth = Math.Clamp((int)Math.Round(Width), 1, 16384);
            var outputHeight = Math.Clamp((int)Math.Round(Height), 1, 16384);
            using var rendered = await PhotoRenderer.RenderAsync(
                SourceItem.FilePath,
                outputWidth,
                outputHeight,
                Rotation);

            var fileName = BuildFileName(rendered.Extension);
            if (overwrite)
            {
                return await Task.Run(() => _library.OverwriteEdited(SourceItem.FilePath, rendered.Stream, fileName));
            }

            return await Task.Run(() => _library.SaveEditedAsNew(rendered.Stream, fileName));
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnWidthChanged(double value)
    {
        if (!_updatingSize && KeepAspectRatio && OriginalWidth > 0)
        {
            _updatingSize = true;
            Height = Math.Max(1, Math.Round(value * OriginalHeight / OriginalWidth));
            _updatingSize = false;
        }

        NotifySizePreview();
    }

    partial void OnHeightChanged(double value)
    {
        if (!_updatingSize && KeepAspectRatio && OriginalHeight > 0)
        {
            _updatingSize = true;
            Width = Math.Max(1, Math.Round(value * OriginalWidth / OriginalHeight));
            _updatingSize = false;
        }

        NotifySizePreview();
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(HasChanges));

    partial void OnRotationChanged(double value)
    {
        var normalized = NormalizeRotation(value);
        if (Math.Abs(normalized - value) > 0.001)
        {
            Rotation = normalized;
            return;
        }

        OnPropertyChanged(nameof(RotationLabel));
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnZoomChanged(double value)
    {
        var clamped = Math.Clamp(value, ClickZoom.Min, ClickZoom.Max);
        if (Math.Abs(clamped - value) > 0.0001)
        {
            Zoom = clamped;
            return;
        }

        OnPropertyChanged(nameof(ZoomLabel));
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnKeepAspectRatioChanged(bool value)
    {
        if (value)
        {
            OnWidthChanged(Width);
        }
    }

    private string BuildFileName(string extension)
    {
        var stem = string.IsNullOrWhiteSpace(Name) ? OriginalName : Name.Trim();
        foreach (var character in Path.GetInvalidFileNameChars())
        {
            stem = stem.Replace(character, '-');
        }

        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = OriginalName;
        }

        return stem + extension;
    }

    private void NotifySizePreview()
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(PreviewScaleX));
        OnPropertyChanged(nameof(PreviewScaleY));
    }

    private void NotifyPreview()
    {
        NotifySizePreview();
        OnPropertyChanged(nameof(ZoomLabel));
        OnPropertyChanged(nameof(RotationLabel));
        OnPropertyChanged(nameof(SizeHint));
    }

    private static double NormalizeRotation(double value)
    {
        var normalized = value % 360;
        if (normalized < 0)
        {
            normalized += 360;
        }

        return Math.Round(normalized, 1);
    }

    private static async Task<(int Width, int Height)> ReadPixelSizeAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
        }

        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }
}
