namespace PersonalMediaPlayer.Core.Storage;

public enum ConnectFolderResult
{
    Connected,
    AlreadyConnected,
    NotAFolder,
    InsideLibrary,
    CoversLibrary,
    Overlaps
}

public sealed class ConnectedFolderScan
{
    public ConnectedFolderScan(int added, int alreadyThere, int missingFolders)
    {
        Added = added;
        AlreadyThere = alreadyThere;
        MissingFolders = missingFolders;
    }

    public int Added { get; }

    public int AlreadyThere { get; }

    public int MissingFolders { get; }
}

public enum ConnectedFileProbe
{
    Ignore,
    Wait,
    Ready
}

public readonly record struct ConnectedLinkMove(string OldPath, string NewPath);

public static class ConnectedFileGrowth
{
    public static readonly TimeSpan QuietPeriod = TimeSpan.FromSeconds(5);

    public static bool IsStable(ref long lastLength, ref DateTime sinceUtc, long length, DateTime now)
    {
        if (length <= 0)
        {
            lastLength = 0;
            sinceUtc = default;
            return false;
        }

        if (sinceUtc == default || length != lastLength)
        {
            lastLength = length;
            sinceUtc = now;
            return false;
        }

        return now - sinceUtc >= QuietPeriod;
    }
}
