using System.Text.RegularExpressions;

namespace PersonalMediaPlayer.App.Playback;

internal static class StreamThumbnail
{
    private static readonly Regex IdPattern = new(@"(?:v=|youtu\.be/|shorts/|embed/|live/)([A-Za-z0-9_-]{11})", RegexOptions.Compiled);

    internal static string? Choose(string? page, Uri? lookedUp)
    {
        if (ForPage(page) is string youtube)
        {
            return youtube;
        }

        if (lookedUp is Uri uri && uri.Scheme is "http" or "https" && !string.IsNullOrEmpty(uri.Host))
        {
            return uri.AbsoluteUri;
        }

        return null;
    }

    internal static string? ForPage(string? page)
    {
        var id = YoutubeId(page);
        return id is null ? null : "https://i.ytimg.com/vi/" + id + "/hqdefault.jpg";
    }

    private static string? YoutubeId(string? page)
    {
        if (string.IsNullOrWhiteSpace(page))
        {
            return null;
        }

        var match = IdPattern.Match(page);
        return match.Success ? match.Groups[1].Value : null;
    }
}
