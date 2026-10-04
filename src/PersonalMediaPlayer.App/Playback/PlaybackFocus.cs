namespace PersonalMediaPlayer.App.Playback;

internal interface IPlaybackSource
{
    void PauseForOther();
}

internal static class PlaybackFocus
{
    private static readonly List<WeakReference<IPlaybackSource>> Sources = [];
    private static WeakReference<IPlaybackSource>? _owner;

    public static void Register(IPlaybackSource source)
    {
        Prune();
        if (Alive().Any(item => ReferenceEquals(item, source)))
        {
            return;
        }

        Sources.Add(new WeakReference<IPlaybackSource>(source));
    }

    public static void Claim(IPlaybackSource source)
    {
        Register(source);
        _owner = new WeakReference<IPlaybackSource>(source);
        foreach (var other in Alive())
        {
            if (!ReferenceEquals(other, source))
            {
                other.PauseForOther();
            }
        }
    }

    public static void PauseOthers(IPlaybackSource? keep)
    {
        foreach (var other in Alive())
        {
            if (!ReferenceEquals(other, keep))
            {
                other.PauseForOther();
            }
        }
    }

    public static bool HeldBySomeoneElse(IPlaybackSource source)
        => _owner?.TryGetTarget(out var owner) == true && !ReferenceEquals(owner, source);

    internal static void Clear()
    {
        Sources.Clear();
        _owner = null;
    }

    private static IEnumerable<IPlaybackSource> Alive()
    {
        foreach (var source in Sources.ToArray())
        {
            if (source.TryGetTarget(out var item))
            {
                yield return item;
            }
        }
    }

    private static void Prune()
    {
        Sources.RemoveAll(source => !source.TryGetTarget(out _));
    }
}
