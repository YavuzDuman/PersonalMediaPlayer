using PersonalMediaPlayer.App.Subtitles;
using Xunit;

namespace PersonalMediaPlayer.Tests;

public class WordReviewTests
{
    [Fact]
    public void TheWordIsCoveredInsideTheSentence()
    {
        Assert.Equal("_____ there.", WordReview.Prompt("Hello there.", "Hello"));
        Assert.Equal("Hello, _____!", WordReview.Prompt("Hello, world!", "world"));
        Assert.Equal("_____ cat and _____ dog", WordReview.Prompt("The cat and the dog", "the"));
        Assert.Equal("They have long _____.", WordReview.Prompt("They have long trunks.", "trunk"));
        Assert.Equal("Please _____ stop.", WordReview.Prompt("Please don't stop.", "don't"));
    }

    [Fact]
    public void ALineThatIsOnlyTheWordStaysHidden()
    {
        Assert.Equal(WordReview.LinePrompt, WordReview.Prompt("Hello there.", "Hello there."));
        Assert.Equal(WordReview.LinePrompt, WordReview.Prompt("  hello there. ", "Hello there."));
        Assert.Equal(WordReview.LinePrompt, WordReview.Prompt("", "hello"));
        Assert.Equal("A different sentence.", WordReview.Prompt("A different sentence.", "missing"));
        Assert.DoesNotContain("merhaba", WordReview.Prompt("Hello there.", "Hello"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShuffleKeepsEveryWordAndRepeatsForTheSameSeed()
    {
        var words = new[]
        {
            Word("one"),
            Word("two"),
            Word("three"),
            Word("four")
        };

        var first = WordReview.Shuffle(words, new Random(7));
        var second = WordReview.Shuffle(words, new Random(7));

        Assert.Equal(first.Select(word => word.English), second.Select(word => word.English));
        Assert.Equal(words.Select(word => word.English).OrderBy(name => name), first.Select(word => word.English).OrderBy(name => name));
    }

    private static SavedWord Word(string english) => new() { English = english, Sentence = english };
}
