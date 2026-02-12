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
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.True(result, $"Should identify empty/tag-only content as removable: '{input}'"); 
    }

    [Theory]
    [InlineData("♪ Jingle Bells ♪", "Jingle Bells")]  // Strip music symbols
    [InlineData("♪", "")]                            // Strip pure music
    [InlineData("[groans]", "")]                     // Strip sound effects
    [InlineData("(laughter)", "")]                   // Strip sound effects
    [InlineData("Hello [groans] World", "Hello  World")] // Strip inline effects
    [InlineData("Captioning by CaptionMax", "")]     // Strip credits
    [InlineData("Synced by JohnDoe", "")]            // Strip credits
    [InlineData("www.google.com", "")]               // Strip URLs
    [InlineData("https://foo.com", "")]              // Strip URLs
    [InlineData("Regular text", "Regular text")]     // Preserve normal text
    [InlineData("Hello. (Note: this might be stripped)", "Hello.")] // Parenthetical stripped
    public void RemoveMarkup_ShouldStripPoisonContent(string input, string expected)
    {
        var result = SubtitleFormatterService.RemoveMarkup(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("m 0 0 l 1 0 l 1 -1 l 0 -1 l 0 0y")] // The specific "weird string" crash case (high density)
    [InlineData("m 0 0 l 1 0 n 50 50 l 40 40 0o")]    // Another corrupted case
    public void IsAssDrawingCommand_ShouldReturnTrue_ForHighDensityDrawingWithGarbage(string input)
    {
        // These strings contain "0y" or "0o" which are NOT valid commands/numbers.
        // But the density of valid commands/numbers is > 80%, so it should be detected as a drawing.
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.True(result, $"Failed to identify high-density drawing with garbage: {input}");
    }

    [Theory]
    [InlineData("m 100 people live here")]     // "m"(cmd), "100"(num), tokens=5. Ratio=0.4. Safe.
    [InlineData("Room 101")]                   // "Room", "101"(num). Ratio=0.5. Safe.
    [InlineData("Plan 9 from Outer Space")]    // "Plan", "9"(num). Ratio=0.2. Safe.
    [InlineData("m is a letter")]              // "m"(cmd). Ratio=0.25 (1/4). Safe.
    [InlineData("100 years ago")]              // "100"(num). Ratio=0.33. Safe.
    [InlineData("1 2 3 go")]                   // 1,2,3(num). Ratio=0.75. Safe (Threshold > 0.8).
    public void IsAssDrawingCommand_ShouldReturnFalse_ForDialogueResemblingCommands(string input)
    {
        var result = SubtitleFormatterService.IsAssDrawingCommand(input);
        Assert.False(result, $"Incorrectly identified dialogue as drawing: {input}");
    }

    [Theory]
    [InlineData("{\\an7}", "")]
    [InlineData("{\\pos(100,100)}Hello {\\c&H00FFFF&}World", "Hello World")]
    [InlineData("Text {with valid comment} inside", "Text inside")]
    [InlineData("Mixed {\\b1}Bold{\\b0} and {\\i1}Italic{\\i0}", "Mixed Bold and Italic")]
    [InlineData("Line1\\NLine2", "Line1 Line2")]
    [InlineData("Space\\hBetween", "Space Between")]
    [InlineData("Visible { literal brace", "Visible { literal brace")] // Unclosed brace should be preserved
    [InlineData("Brace } shown", "Brace } shown")] // } without { is preserved as literal
    public void RemoveMarkup_ShouldHandleMixedContent(string input, string expected)
    {
        var result = SubtitleFormatterService.RemoveMarkup(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Hello", false)]
    [InlineData("A", false)]
    [InlineData("I", false)]
    [InlineData("z", true)]
    [InlineData("Z", true)]
    [InlineData("!", true)]
    [InlineData("  ", true)]
    [InlineData("", true)]
    [InlineData("1", false)]
    public void IsMeaningless_ShouldCorrectIdentifyFlares(string input, bool expected)
    {
        var result = SubtitleFormatterService.IsMeaningless(input);
        Assert.Equal(expected, result);
    }
}
