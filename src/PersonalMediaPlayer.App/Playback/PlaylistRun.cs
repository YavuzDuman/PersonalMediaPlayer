namespace PersonalMediaPlayer.App.Playback;

internal enum PlaylistRepeat
{
    Off,
    One,
    All
}

internal enum PlaylistStepKind
{
    Stop,
    Replay,
    Open
}

internal readonly record struct PlaylistStep(PlaylistStepKind Kind, int Index);

internal sealed class PlaylistRun
{
    private readonly List<string> _order = [];
    private int _place = -1;
    private string _eligible = string.Empty;

    public bool Shuffle { get; private set; }

    public PlaylistRepeat Repeat { get; private set; }

    internal IReadOnlyList<string> Order => _order;

    public string ShuffleLabel => Shuffle ? "Shuffle on" : "Shuffle";

    public string RepeatLabel => Repeat switch
    {
        PlaylistRepeat.One => "Repeat one",
        PlaylistRepeat.All => "Repeat playlist",
        _ => "Repeat off"
    };

    public string RepeatGlyph => Repeat == PlaylistRepeat.One ? "\uE1CC" : "\uE1CD";

    public string Summary
    {
        get
        {
            var order = Shuffle
                ? "Random order. Each video plays once before one plays again."
                : "Saved order.";
            var repeat = Repeat switch
            {
                PlaylistRepeat.One => " Repeating this video.",
                PlaylistRepeat.All => " Repeating the playlist.",
                _ => " Repeat is off."
            };
            return order + repeat;
        }
    }

    public string? ActiveNote
    {
        get
        {
            if (!Shuffle && Repeat == PlaylistRepeat.Off)
            {
                return null;
            }

            var parts = new List<string>();
            if (Shuffle)
            {
                parts.Add("Shuffle");
            }

            if (Repeat == PlaylistRepeat.One)
            {
                parts.Add("Repeat one");
            }
            else if (Repeat == PlaylistRepeat.All)
            {
                parts.Add("Repeat playlist");
            }

            return string.Join(" · ", parts);
        }
    }

    public void SetShuffle(bool on)
    {
        if (Shuffle == on)
        {
            return;
        }

        Shuffle = on;
        _order.Clear();
        _place = -1;
        _eligible = string.Empty;
    }

    public void CycleRepeat()
    {
        Repeat = Repeat switch
        {
            PlaylistRepeat.Off => PlaylistRepeat.All,
            PlaylistRepeat.All => PlaylistRepeat.One,
            _ => PlaylistRepeat.Off
        };
    }

    public void CatchUp(IReadOnlyList<string> keys, Func<int, bool> include, int current, Random random)
        => Align(keys, include, current, random);

    public bool HasMove(IReadOnlyList<string> keys, Func<int, bool> include, int current, bool forward)
    {
        var step = Clone().Move(keys, include, current, playNext: true, fromEnd: false, forward, new Random(0));
        return step.Kind == PlaylistStepKind.Open;
    }

    /// <summary>
    /// True when Next has a waiting video, or another playlist video to open.
    /// A playlist that would only replay the current video waits for the end of that video.
    /// </summary>
    public static bool CanAdvance(int queueCount, Playlist? list, PlaylistRun? run, int index, bool unwatchedOnly)
    {
        if (queueCount > 0)
        {
            return true;
        }

        if (list is null || run is null || list.Videos.Count == 0)
        {
            return false;
        }

        return run.HasMove(
            Keys(list),
            item => Include(list, item, index, unwatchedOnly),
            index,
            forward: true);
    }

    public PlaylistStep Move(
        IReadOnlyList<string> keys,
        Func<int, bool> include,
        int current,
        bool playNext,
        bool fromEnd,
        bool forward,
        Random random)
    {
        Align(keys, include, current, random);
        if (fromEnd && Repeat == PlaylistRepeat.One && (uint)current < (uint)keys.Count)
        {
            return new PlaylistStep(PlaylistStepKind.Replay, current);
        }

        if (fromEnd && Repeat == PlaylistRepeat.Off && !playNext)
        {
            return new PlaylistStep(PlaylistStepKind.Stop, -1);
        }

        return forward
            ? Advance(keys, include, current, random)
            : Retreat(keys, include, current);
    }

    public static string Key(bool online, string location)
        => (online ? "page:" : "file:") + location.Trim().ToLowerInvariant();

    public static List<string> Keys(Playlist list)
    {
        var keys = new List<string>(list.Videos.Count);
        foreach (var entry in list.Videos)
        {
            keys.Add(Key(entry.Resolve, entry.Location));
        }

        return keys;
    }

    public static bool Include(Playlist list, int index, int current, bool unwatchedOnly)
    {
        if ((uint)index >= (uint)list.Videos.Count)
        {
            return false;
        }

        var entry = list.Videos[index];
        if (!entry.IsPlayable)
        {
            return false;
        }

        if (index == current)
        {
            return true;
        }

        return !unwatchedOnly || !entry.Watched;
    }

    private PlaylistStep Advance(IReadOnlyList<string> keys, Func<int, bool> include, int current, Random random)
    {
        if (!Shuffle)
        {
            return StepSaved(keys, include, current, forward: true);
        }

        var found = Find(keys, include, _place + 1, 1);
        if (found is int place)
        {
            _place = place;
            return OpenAt(Resolve(keys, include, _order[place]), current);
        }

        if (Repeat != PlaylistRepeat.All)
        {
            return new PlaylistStep(PlaylistStepKind.Stop, -1);
        }

        var next = Eligible(keys, include);
        Mix(next, random);
        var currentKey = (uint)current < (uint)keys.Count ? keys[current] : null;
        if (next.Count > 1 && next[0] == currentKey)
        {
            // The video that just finished waits until the others have started the new round.
            (next[0], next[1]) = (next[1], next[0]);
        }

        _order.Clear();
        _order.AddRange(next);
        _place = 0;
        return _order.Count == 0
            ? new PlaylistStep(PlaylistStepKind.Stop, -1)
            : OpenAt(Resolve(keys, include, _order[0]), current);
    }

    private PlaylistStep Retreat(IReadOnlyList<string> keys, Func<int, bool> include, int current)
    {
        if (!Shuffle)
        {
            return StepSaved(keys, include, current, forward: false);
        }

        var found = Find(keys, include, _place - 1, -1);
        if (found is int place)
        {
            _place = place;
            return OpenAt(Resolve(keys, include, _order[place]), current);
        }

        if (Repeat != PlaylistRepeat.All)
        {
            return new PlaylistStep(PlaylistStepKind.Stop, -1);
        }

        var last = Find(keys, include, _order.Count - 1, -1);
        if (last is not int end)
        {
            return new PlaylistStep(PlaylistStepKind.Stop, -1);
        }

        _place = end;
        return OpenAt(Resolve(keys, include, _order[end]), current);
    }

    private PlaylistStep StepSaved(IReadOnlyList<string> keys, Func<int, bool> include, int current, bool forward)
    {
        var step = forward ? 1 : -1;
        var start = current < 0
            ? forward ? 0 : keys.Count - 1
            : current + step;
        for (var i = start; i >= 0 && i < keys.Count; i += step)
        {
            if (include(i))
            {
                return OpenAt(i, current);
            }
        }

        if (Repeat != PlaylistRepeat.All)
        {
            return new PlaylistStep(PlaylistStepKind.Stop, -1);
        }

        var wrap = forward ? 0 : keys.Count - 1;
        for (var i = wrap; i >= 0 && i < keys.Count; i += step)
        {
            if (include(i))
            {
                return OpenAt(i, current);
            }
        }

        return new PlaylistStep(PlaylistStepKind.Stop, -1);
    }

    private void Align(IReadOnlyList<string> keys, Func<int, bool> include, int current, Random random)
    {
        var signature = Signature(keys, include);
        var currentKey = (uint)current < (uint)keys.Count ? keys[current] : null;
        if (!Shuffle)
        {
            _order.Clear();
            for (var i = 0; i < keys.Count; i++)
            {
                if (include(i))
                {
                    _order.Add(keys[i]);
                }
            }

            _place = currentKey is null ? -1 : _order.IndexOf(currentKey);
            _eligible = signature;
            return;
        }

        if (_order.Count > 0 && _eligible == signature && (currentKey is null || _order.Contains(currentKey)))
        {
            if (currentKey is not null)
            {
                _place = _order.IndexOf(currentKey);
            }

            return;
        }

        var rest = Eligible(keys, include);
        if (currentKey is not null)
        {
            rest.Remove(currentKey);
        }

        Mix(rest, random);
        _order.Clear();
        _place = -1;
        if (currentKey is not null && (uint)current < (uint)keys.Count && include(current))
        {
            _order.Add(currentKey);
            _place = 0;
        }

        _order.AddRange(rest);
        _eligible = signature;
    }

    private int? Find(IReadOnlyList<string> keys, Func<int, bool> include, int start, int step)
    {
        for (var place = start; place >= 0 && place < _order.Count; place += step)
        {
            if (Resolve(keys, include, _order[place]) >= 0)
            {
                return place;
            }
        }

        return null;
    }

    private static int Resolve(IReadOnlyList<string> keys, Func<int, bool> include, string key)
    {
        for (var i = 0; i < keys.Count; i++)
        {
            if (include(i) && keys[i] == key)
            {
                return i;
            }
        }

        return -1;
    }

    private static List<string> Eligible(IReadOnlyList<string> keys, Func<int, bool> include)
    {
        var eligible = new List<string>();
        for (var i = 0; i < keys.Count; i++)
        {
            if (include(i))
            {
                eligible.Add(keys[i]);
            }
        }

        return eligible;
    }

    private static string Signature(IReadOnlyList<string> keys, Func<int, bool> include)
    {
        var names = Eligible(keys, include);
        names.Sort(StringComparer.Ordinal);
        return string.Join("\n", names);
    }

    private static void Mix(List<string> items, Random random)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var swap = random.Next(i + 1);
            (items[i], items[swap]) = (items[swap], items[i]);
        }
    }

    private static PlaylistStep OpenAt(int index, int current)
        => index < 0
            ? new PlaylistStep(PlaylistStepKind.Stop, -1)
            : index == current
                ? new PlaylistStep(PlaylistStepKind.Replay, index)
                : new PlaylistStep(PlaylistStepKind.Open, index);

    private PlaylistRun Clone()
    {
        var copy = new PlaylistRun
        {
            Shuffle = Shuffle,
            Repeat = Repeat,
            _place = _place,
            _eligible = _eligible
        };
        copy._order.AddRange(_order);
        return copy;
    }
}

internal static class PlaylistRuns
{
    private static readonly Dictionary<string, PlaylistRun> Runs = new(StringComparer.Ordinal);

    internal static Random Random { get; } = new();

    internal static PlaylistRun For(string playlistId)
    {
        if (!Runs.TryGetValue(playlistId, out var run))
        {
            run = new PlaylistRun();
            Runs[playlistId] = run;
        }

        return run;
    }
}
