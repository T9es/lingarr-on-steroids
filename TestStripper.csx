// Quick test to verify ASS stripping logic on extracted subtitle
using System.Text.RegularExpressions;

var inputPath = args.Length > 0 ? args[0] : @"E:\openllmvtuber\lingarr-on-steroids\EXAMPLE EPISODE\extracted_test.srt";
var outputPath = Path.Combine(Path.GetDirectoryName(inputPath)!, "stripped_output.srt");

Console.WriteLine($"Processing: {inputPath}");

var lines = File.ReadAllLines(inputPath);
var output = new List<string>();
var subtitleBuffer = new List<string>();
int originalCount = 0;
int keptCount = 0;
int removedCount = 0;

for (int i = 0; i < lines.Length; i++)
{
    var line = lines[i];
    
    // If we hit an empty line, process the buffered subtitle
    if (string.IsNullOrWhiteSpace(line))
    {
        if (subtitleBuffer.Count >= 3)
        {
            originalCount++;
            // Buffer: [0] = number, [1] = timecode, [2+] = text
            var textLines = subtitleBuffer.Skip(2).ToList();
            var combinedText = string.Join(" ", textLines);
            var cleanedText = string.Join(" ", textLines.Select(RemoveMarkup));
            
            if (!IsAssDrawingCommand(combinedText))
            {
                keptCount++;
                foreach (var bufferLine in subtitleBuffer)
                    output.Add(bufferLine);
                output.Add("");
            }
            else
            {
                removedCount++;
                if (removedCount <= 5)
                {
                    Console.WriteLine($"  REMOVED #{subtitleBuffer[0]}: \"{cleanedText.Substring(0, Math.Min(50, cleanedText.Length))}...\"");
                }
            }
        }
        subtitleBuffer.Clear();
    }
    else
    {
        subtitleBuffer.Add(line);
    }
}

// Process any remaining buffer
if (subtitleBuffer.Count >= 3)
{
    originalCount++;
    var textLines = subtitleBuffer.Skip(2).ToList();
    var combinedText = string.Join(" ", textLines);
    if (!IsAssDrawingCommand(combinedText))
    {
        keptCount++;
        foreach (var bufferLine in subtitleBuffer)
            output.Add(bufferLine);
    }
    else
    {
        removedCount++;
    }
}

File.WriteAllLines(outputPath, output);

Console.WriteLine($"\n=== RESULTS ===");
Console.WriteLine($"Original subtitles: {originalCount}");
Console.WriteLine($"Kept (valid text):  {keptCount}");
Console.WriteLine($"Removed (junk):     {removedCount}");
Console.WriteLine($"\nOutput written to: {outputPath}");

// Show first 10 kept subtitles
Console.WriteLine($"\n=== FIRST 10 KEPT SUBTITLES ===");
var keptSubtitles = new List<string>();
subtitleBuffer.Clear();
foreach (var line in output)
{
    if (string.IsNullOrWhiteSpace(line) && subtitleBuffer.Count >= 3)
    {
        var text = string.Join(" ", subtitleBuffer.Skip(2).Select(RemoveMarkup));
        keptSubtitles.Add($"#{subtitleBuffer[0]}: {text.Substring(0, Math.Min(60, text.Length))}");
        subtitleBuffer.Clear();
    }
    else if (!string.IsNullOrWhiteSpace(line))
    {
        subtitleBuffer.Add(line);
    }
}
foreach (var s in keptSubtitles.Take(10))
    Console.WriteLine($"  {s}");

// ============ FUNCTIONS FROM SubtitleFormatterService.cs ============

static string RemoveMarkup(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;
    
    // Remove SSA/ASS style tags: {\\...}
    string cleaned = Regex.Replace(input, @"\{.*?\}", string.Empty, RegexOptions.Singleline);
    
    // Remove HTML-style tags: <...>
    cleaned = Regex.Replace(cleaned, @"<.*?>", string.Empty, RegexOptions.Singleline);
    
    // Replace SSA line breaks with spaces
    cleaned = cleaned.Replace("\\N", " ").Replace("\\n", " ");
    
    // Replace tab characters
    cleaned = cleaned.Replace("\\t", " ").Replace("\t", " ");
    
    // Collapse multiple whitespace into a single space
    cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
    
    return cleaned.Trim();
}

static bool IsAssDrawingCommand(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return true;
    
    string cleaned = RemoveMarkup(input);
    
    if (string.IsNullOrWhiteSpace(cleaned)) return true;
    
    // Single character check
    if (cleaned.Length == 1)
    {
        char c = cleaned[0];
        if (char.IsDigit(c)) return false;
        if ("IAaOo".Contains(c)) return false;
        return true;  // Garbage single letter
    }
    
    // ASS drawing commands check
    var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    bool hasMoveCommand = false;
    int numberCount = 0;
    
    foreach (var token in tokens)
    {
        if (token.Length == 1 && "mnlbspc".Contains(char.ToLowerInvariant(token[0])))
        {
            if (char.ToLowerInvariant(token[0]) == 'm') hasMoveCommand = true;
            continue;
        }
        
        if (double.TryParse(token, System.Globalization.NumberStyles.Any, 
            System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            numberCount++;
            continue;
        }
        
        // Found a word - not a drawing command
        return false;
    }
    
    return hasMoveCommand || numberCount >= 2;
}
