using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class LearningModeTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _directory;
    private readonly string _store;

    public LearningModeTests()
    {
        Monitor.Enter(Gate);
        _directory = Path.Combine(Path.GetTempPath(), "pmp-learning-mode-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _store = Path.Combine(_directory, LearningMode.FileName);
        LearningMode.StoreOverride = _store;
    }

    public void Dispose()
    {
        try
        {
            LearningMode.StoreOverride = null;
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        finally
        {
            Monitor.Exit(Gate);
        }
    }

    [Fact]
    public void AMissingFileStaysOnAndSavingOnDoesNotCreateIt()
    {
        Assert.True(LearningMode.Enabled);
        Assert.False(File.Exists(_store));
        LearningMode.Save(true);
        Assert.False(File.Exists(_store));
        Assert.True(LearningMode.Enabled);
    }

    [Fact]
    public void TurningItOffWritesFalseAndReloads()
    {
        Assert.True(LearningMode.Save(false));
        Assert.Equal("false", File.ReadAllText(_store));
        LearningMode.StoreOverride = _store;
        Assert.False(LearningMode.Enabled);

        File.WriteAllText(_store, "true");
        LearningMode.StoreOverride = _store;
        Assert.True(LearningMode.Enabled);
        Assert.Equal("true", File.ReadAllText(_store));
    }

    [Fact]
    public void GarbageReadsAsOnAndSavingOnLeavesIt()
    {
        File.WriteAllText(_store, "nope");
        LearningMode.StoreOverride = _store;
        Assert.True(LearningMode.Enabled);
        var bytes = File.ReadAllBytes(_store);
        LearningMode.Save(true);
        Assert.Equal(bytes, File.ReadAllBytes(_store));
    }

    [Fact]
    public void TurningItOffThenOnWritesTrue()
    {
        LearningMode.Save(false);
        LearningMode.Save(true);
        Assert.Equal("true", File.ReadAllText(_store));
        LearningMode.StoreOverride = _store;
        Assert.True(LearningMode.Enabled);
    }

    [Fact]
    public void TheSameChoiceDoesNotRewrite()
    {
        LearningMode.Save(false);
        var bytes = File.ReadAllBytes(_store);
        File.SetLastWriteTimeUtc(_store, DateTime.UtcNow.AddHours(-2));
        var parked = File.GetLastWriteTimeUtc(_store);
        LearningMode.Save(false);
        Assert.Equal(bytes, File.ReadAllBytes(_store));
        Assert.Equal(parked, File.GetLastWriteTimeUtc(_store));
    }

    [Fact]
    public void ARealChangeIsAnnouncedOnce()
    {
        var changes = 0;
        Action onChanged = () => changes++;
        LearningMode.Changed += onChanged;
        try
        {
            LearningMode.Save(true);
            Assert.Equal(0, changes);
            Assert.False(File.Exists(_store));

            LearningMode.Save(false);
            Assert.Equal(1, changes);
            LearningMode.Save(false);
            Assert.Equal(1, changes);

            LearningMode.Save(true);
            Assert.Equal(2, changes);
            LearningMode.Save(true);
            Assert.Equal(2, changes);
        }
        finally
        {
            LearningMode.Changed -= onChanged;
        }
    }

    [Fact]
    public void ABlockedFolderKeepsTheChoiceAndReportsTheFailure()
    {
        var blocker = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocker, "x");
        LearningMode.StoreOverride = Path.Combine(blocker, LearningMode.FileName);
        var changes = 0;
        Action onChanged = () => changes++;
        LearningMode.Changed += onChanged;
        try
        {
            Assert.False(LearningMode.Save(false));
            Assert.Equal(1, changes);
            Assert.False(LearningMode.Enabled);
            Assert.False(string.IsNullOrWhiteSpace(LearningMode.SaveFailedMessage));
            Assert.Equal("x", File.ReadAllText(blocker));
            Assert.False(Directory.Exists(blocker));
        }
        finally
        {
            LearningMode.Changed -= onChanged;
        }
    }

    [Fact]
    public void TurningItOffLeavesNearbySavedWords()
    {
        var words = Path.Combine(_directory, "saved-words.json");
        var payload = new byte[] { 0x7B, 0x22, 0x6B, 0x22, 0x3A, 0x31, 0x7D };
        File.WriteAllBytes(words, payload);
        LearningMode.Save(false);
        Assert.Equal("false", File.ReadAllText(_store));
        Assert.Equal(payload, File.ReadAllBytes(words));
        Assert.Equal(
            new[] { "learning-mode.txt", "saved-words.json" },
            Directory.GetFiles(_directory).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }
}
