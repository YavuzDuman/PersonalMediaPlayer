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
