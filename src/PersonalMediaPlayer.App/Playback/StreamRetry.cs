namespace PersonalMediaPlayer.App.Playback;

internal static class StreamRetry
{
    // A follow-up attempt keeps the spent retry. A new stream still starts at zero.
    internal static int Clear() => 1;

    internal static bool TrySpend(ref int retries)
    {
        if (retries >= 1)
        {
            return false;
        }

        retries++;
        return true;
    }
}
