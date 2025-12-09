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
    [InlineData("I")]  // Common pronoun - preserved
    [InlineData("1")]  // Single digit - preserved
    [InlineData("2")]  // Single digit - preserved
    [InlineData("9")]  // Single digit - preserved
    public void IsAssDrawingCommand_ShouldReturnFalse_ForValidSingleLetters(string input)
    {
        // Only "I" (common pronoun) and single digits are preserved.
        // Letters like "a", "A", "O" are filtered as they are often garbage from
        // vertical text effects or too rare/poetic to risk keeping noise.
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.False(result, $"Incorrectly identified valid single letter as garbage: {input}");
    }

    [Fact]
    public void IsAssDrawingCommand_ShouldReturnTrue_ForMassiveNestedTagDrawing()
    {
        // User provided example of a massive drawing command with complex nested tags
        var input = "<font face=\"Cabin\"><font size=\"45\">{\\an7}<font color=\"#eae1a5\">m 352.23 56.19 b 352.23 74.1 352.53 92.01 352.13 109.91 351.83 122.97 352.8 136.13 349.7 149.08 348.79 152.87 349.99 157.12 349.38 161.02 348.04 169.68 351.59 179 345.69 187.13 344.65 188.57 345.7 191.39 345.41 193.52 344.84 197.67 341.43 199.2 338.53 196.29 336.61 194.37 334.78 191.39 334.66 188.8 333.97 174.78 333.74 160.72 333.5 146.68 333.02 118.54 332.59 90.41 332.24 62.27 332.16 56.32 332.45 50.36 332.61 44.41 332.73 40.19 330.71 38.19 326.52 37.97 315.13 37.37 303.73 36.76 292.35 35.94 287.38 35.58 284.08 30.83 284.49 25.28 284.76 21.61 288.31 18.83 292.85 18.59 310.88 17.63 328.89 17.78 346.92 18.75 355.97 19.24 365.07 18.64 374.15 18.84 377.25 18.91 380.97 18.51 381.5 23.52 382.47 32.77 382.65 33.48 375.94 34.95 370.5 36.13 364.67 35.43 359.16 36.4 356.97 36.78 354.05 39.05 353.43 41.06 351.96 45.87 351.67 51.03 350.91 56.05 351.35 56.1 351.79 56.14 352.23 56.19</font></font></font>";
        
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.True(result, "Failed to identify massive nested tag drawing as garbage");
    }

    [Fact]
    public void IsAssDrawingCommand_ShouldReturnFalse_ForNormalDialogueWithTags()
    {
        // User provided example of normal dialogue
        var input = "<font face=\"Boopee\"><font size=\"66\"><font color=\"#141313\">{\\an1}Na-Nah you gotta keep it to yourself until you do it</font></font></font>";
        
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.False(result, "Incorrectly identified normal dialogue as garbage");
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
