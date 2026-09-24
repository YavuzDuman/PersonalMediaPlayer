using PersonalMediaPlayer.Core.Models;

namespace PersonalMediaPlayer.App.Capture;

internal sealed record PreviewRequest(MediaItem Item, string HighlightTerm);