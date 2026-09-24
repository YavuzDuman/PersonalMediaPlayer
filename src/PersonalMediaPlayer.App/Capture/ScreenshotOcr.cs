using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace PersonalMediaPlayer.App.Capture;

internal static class ScreenshotOcr
{
    public static async Task<string> ReadAsync(byte[] pngBytes)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        using var prepared = await PrepareAsync(pngBytes);
        var result = await RecognizeBestAsync(prepared.Bitmap);
        return string.Join(
            Environment.NewLine,
            result.Lines.Select(line => line.Text).Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    public static async Task<IReadOnlyList<OcrWordBox>> FindWordsAsync(byte[] pngBytes, string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return [];
        }

        var decoded = await PrepareAsync(pngBytes);
        using var bitmap = decoded.Bitmap;
        var result = await RecognizeBestAsync(bitmap);
        var needle = term.Trim();
        var boxes = new List<OcrWordBox>();
        foreach (var line in result.Lines)
        {
            var words = line.Words.ToArray();
            var claimed = new bool[words.Length];
            for (var start = 0; start < words.Length; start++)
            {
                var joined = string.Empty;
                for (var end = start; end < words.Length; end++)
                {
                    joined = end == start ? words[end].Text : joined + " " + words[end].Text;
                    if (!joined.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        if (joined.Length > needle.Length + 24)
                        {
                            break;
                        }

                        continue;
                    }

                    var matchStart = start;
                    while (matchStart < end && WordsText(words, matchStart + 1, end).Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        matchStart++;
                    }

                    for (var index = matchStart; index <= end; index++)
                    {
                        if (claimed[index])
                        {
                            continue;
                        }

                        claimed[index] = true;
                        var bounds = words[index].BoundingRect;
                        boxes.Add(new OcrWordBox(
                            bounds.X * decoded.ScaleX,
                            bounds.Y * decoded.ScaleY,
                            bounds.Width * decoded.ScaleX,
                            bounds.Height * decoded.ScaleY));
                    }

                    break;
                }
            }
        }

        return boxes;
    }

    private static string WordsText(Windows.Media.Ocr.OcrWord[] words, int start, int end)
    {
        var text = words[start].Text;
        for (var index = start + 1; index <= end; index++)
        {
            text += " " + words[index].Text;
        }

        return text;
    }

    private static IEnumerable<OcrEngine> Engines()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var profile = OcrEngine.TryCreateFromUserProfileLanguages();
        if (profile is not null && seen.Add(profile.RecognizerLanguage.LanguageTag))
        {
            yield return profile;
        }

        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
        {
            if (!seen.Add(language.LanguageTag))
            {
                continue;
            }

            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine is not null)
            {
                yield return engine;
            }
        }
    }

    private static async Task<OcrResult> RecognizeBestAsync(SoftwareBitmap bitmap)
    {
        OcrResult? best = null;
        var bestWords = -1;
        foreach (var engine in Engines())
        {
            using var copy = SoftwareBitmap.Copy(bitmap);
            OcrResult result;
            try
            {
                result = await engine.RecognizeAsync(copy);
            }
            catch
            {
                continue;
            }

            var words = result.Lines.Sum(line => line.Words.Count);
            if (words > bestWords)
            {
                best = result;
                bestWords = words;
            }
        }

        return best ?? throw new InvalidOperationException(
            "Windows has no OCR language installed. Add a language under Settings, Time & language, Language & region.");
    }

    private static async Task<ScaledBitmap> PrepareAsync(byte[] pngBytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(pngBytes.AsBuffer());
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var longest = Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var limit = OcrEngine.MaxImageDimension;
        var fit = 1d;
        if (limit > 0 && longest > 0)
        {
            var enlarged = Math.Min(longest * 2d, limit);
            fit = enlarged / longest;
        }

        var transform = new BitmapTransform { InterpolationMode = BitmapInterpolationMode.Fant };
        if (Math.Abs(fit - 1d) > 0.01)
        {
            transform.ScaledWidth = (uint)Math.Max(1, Math.Floor(decoder.PixelWidth * fit));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Floor(decoder.PixelHeight * fit));
        }

        var pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage);
        var bytes = pixels.DetachPixelData();
        DarkenText(bytes);
        var width = transform.ScaledWidth > 0 ? transform.ScaledWidth : decoder.PixelWidth;
        var height = transform.ScaledHeight > 0 ? transform.ScaledHeight : decoder.PixelHeight;
        var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, (int)width, (int)height, BitmapAlphaMode.Premultiplied);
        bitmap.CopyFromBuffer(bytes.AsBuffer());
        var scaleX = decoder.PixelWidth / (double)Math.Max(1, width);
        var scaleY = decoder.PixelHeight / (double)Math.Max(1, height);
        return new ScaledBitmap(bitmap, scaleX, scaleY);
    }

    private static void DarkenText(byte[] pixels)
    {
        const double contrast = 1.5;
        for (var offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            var blue = pixels[offset];
            var green = pixels[offset + 1];
            var red = pixels[offset + 2];
            var gray = (red * 0.299) + (green * 0.587) + (blue * 0.114);
            var boosted = (int)Math.Round(((gray - 128d) * contrast) + 128d);
            var value = (byte)Math.Clamp(boosted, 0, 255);
            pixels[offset] = value;
            pixels[offset + 1] = value;
            pixels[offset + 2] = value;
        }
    }

    private sealed class ScaledBitmap(SoftwareBitmap bitmap, double scaleX, double scaleY) : IDisposable
    {
        public SoftwareBitmap Bitmap { get; } = bitmap;

        public double ScaleX { get; } = scaleX;

        public double ScaleY { get; } = scaleY;

        public void Dispose() => Bitmap.Dispose();
    }
}

public readonly record struct OcrWordBox(double X, double Y, double Width, double Height);
