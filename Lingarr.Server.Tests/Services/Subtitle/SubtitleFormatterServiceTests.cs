using Lingarr.Server.Services.Subtitle;
using Xunit;

namespace Lingarr.Server.Tests.Services.Subtitle;

public class SubtitleFormatterServiceTests
{
    [Theory]
    [InlineData("m 185.89 20.59 b 193.53 20.98 203.71 21.49")]
    [InlineData("m 70.49 107.91 b 70.49 92.27 70.49 76.63")]
    [InlineData(".54 108.77 207.56 98.21")] // Starts with decimal, no 'm'
    [InlineData("<font face=\"Barthowheel\"><font size=\"66\"><font color=\"#0a0606\"><b>{\\an7}<font color=\"#4a030d\"><font color=\"#b80009\">m 185.89 20.59 b 193.53 20.98")]
    [InlineData("<font\nface=\"Barthowheel\">m 185.89 20.59")] // Multiline tag - expected to fail currently
    public void IsAssDrawingCommand_ShouldReturnTrue_ForDrawingCommands(string input)
    {
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.True(result, $"Failed to identify input as ASS drawing command: {input}");
    }

    [Theory]
    [InlineData("e")]
    [InlineData("l")]
    [InlineData("p")]
    [InlineData("c")]
    [InlineData("s")]
    public void IsAssDrawingCommand_ShouldReturnTrue_ForSingleLetterGarbage(string input)
    {
        // Single letters that are not valid words (I, A) or numbers should be considered garbage/drawing commands
        // or remnants of vertical text requiring removal.
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.True(result, $"Failed to identify single letter garbage: {input}");
    }

    [Theory]
    [InlineData("I")]
    [InlineData("a")]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("O")]
    public void IsAssDrawingCommand_ShouldReturnFalse_ForValidSingleLetters(string input)
    {
        // Valid single letter words or numbers should be preserved.
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.False(result, $"Incorrectly identified valid single letter as garbage: {input}");
    }

    [Theory]
    [InlineData("{\\an7}")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsAssDrawingCommand_ShouldReturnTrue_ForEmptyOrTagOnlyContent(string input)
    {
        // We want to filter these out to avoid empty lines in the output.
        // Current implementation returns false. After fix, it should return true.
        // For reproduction phase, we assert FALSE to confirm current behavior, 
        // OR we can assert TRUE and expect failure (red test).
        // Let's assert TRUE to see it fail, confirming the need for fix.
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.True(result, $"Should identify empty/tag-only content as removable: '{input}'"); 
    }
}
