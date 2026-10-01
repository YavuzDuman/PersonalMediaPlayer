using PersonalMediaPlayer.App.Playback;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class LanguageLabelTests
{
    [Fact]
    public void AudioCodesAndNativeNamesBecomeEnglish()
    {
        Assert.Equal("Bangla", LanguageLabels.ForAudio("bn", "bn"));
        Assert.Equal("Spanish (United States)", LanguageLabels.ForAudio("es-US", "es-US"));
        Assert.Equal("Spanish (Latin America)", LanguageLabels.ForAudio("es-419", "es-419"));
        Assert.Equal("Chinese (Simplified)", LanguageLabels.ForAudio("zh-Hans", "中文"));
        Assert.Equal("Arabic · Dubbed", LanguageLabels.ForAudio("ar", "العربية - dubbed"));
        Assert.Equal("Hindi · Dubbed", LanguageLabels.ForAudio("hi", "हिन्दी - dubbed"));
        Assert.Equal("Japanese · Dubbed", LanguageLabels.ForAudio("ja", "日本語 - dubbed"));
        Assert.Equal("Hungarian · Dubbed", LanguageLabels.ForAudio("hu", "magyar - dubbed"));
        Assert.Equal("German · Dubbed", LanguageLabels.ForAudio("de", "Deutsch - dubbed"));
        Assert.Equal("Dutch (Netherlands) · Dubbed", LanguageLabels.ForAudio("nl-NL", "Nederlands (Nederland) - dubbed"));
        Assert.Equal("English · Original", LanguageLabels.ForAudio("en", "English, original"));
        Assert.Equal("English · Original", LanguageLabels.ForAudio("en", "Default"));
        Assert.Equal("Russian · Dubbed", LanguageLabels.ForAudio("ru", "Русский - dubbed"));
    }

    [Fact]
    public void CaptionNamesStayEnglishAndKeepTheDownloadCode()
    {
        Assert.Equal("Bangla · Automatic", LanguageLabels.ForCaption("bn", "bn", true));
        Assert.Equal("Bangla · Automatic", LanguageLabels.ForCaption("bn", "বাংলা", true));
        Assert.Equal("Spanish (United States) · Automatic", LanguageLabels.ForCaption("es-US", "es-US", true));
        Assert.Equal("English · Automatic", LanguageLabels.ForCaption("en-orig", "English (Original)", true));
        Assert.Equal("English · Automatic", LanguageLabels.ForCaption("en-ar", "English from Arabic", true));
        Assert.Equal("Bangla · Automatic", LanguageLabels.ForCaption("bn-en", "Bangla from English", true));
        Assert.Equal("Hebrew · Automatic", LanguageLabels.ForCaption("iw-ar", null, true));
        Assert.Equal("Chinese (Simplified) · Automatic", LanguageLabels.ForCaption("zh-Hans-en", "Chinese (Simplified) from English", true));
        Assert.Equal("Chinese · Automatic", LanguageLabels.ForCaption("zh", null, true));
        Assert.Equal("Dutch (Netherlands)", LanguageLabels.ForCaption("nl-NL", "Dutch (Netherlands)", false));
        Assert.Equal("Arabic", LanguageLabels.ForCaption("ar", "العربية", false));
        Assert.Equal("en", LanguageLabels.Group("en-orig"));
        Assert.Equal("en", LanguageLabels.Group("en-ar", "English from Arabic"));
        Assert.Equal("bn", LanguageLabels.Group("bn-nl-NL", "Bangla from Dutch (Netherlands)"));
        Assert.Equal("es-US", LanguageLabels.Group("es-US"));
        Assert.Equal("zh-Hans", LanguageLabels.Group("zh-Hans"));
        Assert.Equal("zh-Hans", LanguageLabels.Group("zh-Hans-en", "Chinese (Simplified) from English"));
    }
}
