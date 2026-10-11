namespace RefBoardXIV.Tests;

public class CommandsTests
{
    [Theory]
    [InlineData("", "Toggle")]
    [InlineData("   ", "Toggle")]
    [InlineData("show", "Show")]
    [InlineData("Afficher", "Show")]
    [InlineData(" hide ", "Hide")]
    [InlineData("masquer", "Hide")]
    [InlineData("lock", "Lock")]
    [InlineData("Verrouiller", "Lock")]
    [InlineData("config", "Settings")]
    [InlineData("réglages", "Settings")]
    [InlineData("reglages", "Settings")]
    [InlineData("hello", "Unknown")]
    public void ParsesEnglishAndFrenchWords(string arguments, string expected)
    {
        Assert.Equal(expected, Commands.Parse(arguments).ToString());
    }
}
