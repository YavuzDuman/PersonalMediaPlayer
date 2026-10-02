using PersonalMediaPlayer.App.Download;
using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class StreamLanguageTests : IDisposable
{
    private static readonly object Gate = new();

    private readonly string _store;

    public StreamLanguageTests()
    {
        Monitor.Enter(Gate);
        _store = Path.Combine(Path.GetTempPath(), "pmp-languages-" + Guid.NewGuid().ToString("N") + ".json");
        StreamLanguageSettings.StoreOverride = _store;
    }

    public void Dispose()
    {
        StreamLanguageSettings.StoreOverride = null;
        if (File.Exists(_store))
        {
            File.Delete(_store);
        }

        Monitor.Exit(Gate);
    }

    [Fact]
    public void AMissingFilePrefersOriginalAudioAndSubtitlesOff()
    {
        var preference = StreamLanguageSettings.Load();
        Assert.Null(preference.AudioLanguage);
        Assert.Null(preference.CaptionLanguage);
        Assert.Equal(0, StreamLanguageSettings.AudioIndex(preference.AudioLanguage));
        Assert.Equal(0, StreamLanguageSettings.CaptionIndex(preference.CaptionLanguage));
    }

    [Fact]
    public void ASavedLanguageRoundTripsAndAnUnknownLanguageIsDropped()
    {
        StreamLanguageSettings.Save(new StreamLanguagePreference("tr", "en"));
        var saved = StreamLanguageSettings.Load();
        Assert.Equal("tr", saved.AudioLanguage);
        Assert.Equal("en", saved.CaptionLanguage);
        Assert.Equal("tr", StreamLanguageSettings.LanguageFromIndex(StreamLanguageSettings.AudioIndex("tr")));
        Assert.Null(StreamLanguageSettings.LanguageFromIndex(0));

        File.WriteAllText(_store, """{"Audio":"nope","Captions":"off"}""");
        var dropped = StreamLanguageSettings.Load();
        Assert.Null(dropped.AudioLanguage);
        Assert.Null(dropped.CaptionLanguage);
        Assert.Contains(StreamLanguageSettings.Languages, item => item.Code == "tr" && item.Name == "Turkish");
        Assert.Contains(StreamLanguageSettings.Languages, item => item.Code == "en" && item.Name == "English");
    }

    [Fact]
    public void CaptionPreferencePicksThatLanguageOrStaysOff()
    {
        var tracks = new DownloadSubtitle[]
        {
            new("de", "German", false),
            new("en-orig", "English · Automatic", true),
            new("tr", "Turkish · Automatic", true, true)
        };

        Assert.Equal(-1, StreamLanguageSettings.ChooseCaption(tracks, null, null));
        Assert.Equal(1, StreamLanguageSettings.ChooseCaption(tracks, null, "en"));
        Assert.Equal(2, StreamLanguageSettings.ChooseCaption(tracks, null, "tr"));
        Assert.Equal(-1, StreamLanguageSettings.ChooseCaption(tracks, null, "fr"));
        Assert.Equal(2, StreamLanguageSettings.ChooseCaption(tracks, "tr", "en"));
        Assert.Equal(-1, StreamLanguageSettings.ChooseCaption(tracks, "ja", "en"));
        Assert.Equal(1, StreamLanguageSettings.ChooseCaption(tracks, "en-orig", null));
    }
}
