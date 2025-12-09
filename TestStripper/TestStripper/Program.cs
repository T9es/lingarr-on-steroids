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
var removedExamples = new List<string>();

var lastKeptText = string.Empty;

for (int i = 0; i < lines.Length; i++)
{
    var line = lines[i];
    
    if (string.IsNullOrWhiteSpace(line))
    {
        if (subtitleBuffer.Count >= 3)
        {
            originalCount++;
            var textLines = subtitleBuffer.Skip(2).ToList();
            var combinedText = string.Join(" ", textLines);
            var cleanedText = string.Join(" ", textLines.Select(RemoveMarkup));
            
            if (!IsAssDrawingCommand(combinedText))
            {
                // DEDUPLICATION:
                // If text is identical to last kept line, skip it
                if (cleanedText != lastKeptText)
                {
                    keptCount++;
                    output.Add(subtitleBuffer[0]); // Number
                    output.Add(subtitleBuffer[1]); // Timecode
                    output.Add(cleanedText);       // Cleaned Text
                    output.Add("");
                    lastKeptText = cleanedText;
                }
                else
                {
                    // It's a duplicate - count as removed? or just skip?
                    // Let's count as removed for stats
                    removedCount++;
                }
            }
            else
            {
                removedCount++;
                if (removedExamples.Count < 10)
                    removedExamples.Add($"#{subtitleBuffer[0]}: \"{TruncateText(cleanedText, 40)}\"");
            }
        }
        subtitleBuffer.Clear();
    }
    else
    {
        subtitleBuffer.Add(line);
    }
}

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
        removedCount++;
}

File.WriteAllLines(outputPath, output);

Console.WriteLine($"\n====== RESULTS ======");
Console.WriteLine($"Original entries:   {originalCount}");
Console.WriteLine($"KEPT (valid text):  {keptCount}");
Console.WriteLine($"REMOVED (junk):     {removedCount}");
Console.WriteLine($"Removal rate:       {removedCount * 100.0 / originalCount:F1}%");
Console.WriteLine($"\nOutput: {outputPath}");

Console.WriteLine($"\n====== REMOVED EXAMPLES (first 10) ======");
foreach (var ex in removedExamples)
    Console.WriteLine($"  {ex}");

// Get first 10 kept examples
Console.WriteLine($"\n====== KEPT EXAMPLES (first 10) ======");
subtitleBuffer.Clear();
var keptExamples = new List<string>();
foreach (var line in output)
{
    if (string.IsNullOrWhiteSpace(line) && subtitleBuffer.Count >= 3)
    {
        var text = string.Join(" ", subtitleBuffer.Skip(2).Select(RemoveMarkup));
        keptExamples.Add($"#{subtitleBuffer[0]}: \"{TruncateText(text, 50)}\"");
        subtitleBuffer.Clear();
    }
    else if (!string.IsNullOrWhiteSpace(line))
        subtitleBuffer.Add(line);
}
foreach (var ex in keptExamples.Take(10))
    Console.WriteLine($"  {ex}");

static string TruncateText(string text, int maxLen) => 
    text.Length <= maxLen ? text : text.Substring(0, maxLen) + "...";

static string RemoveMarkup(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return string.Empty;
    string cleaned = Regex.Replace(input, @"\{.*?\}", string.Empty, RegexOptions.Singleline);
    cleaned = Regex.Replace(cleaned, @"<.*?>", string.Empty, RegexOptions.Singleline);
    cleaned = cleaned.Replace("\\N", " ").Replace("\\n", " ");
    cleaned = cleaned.Replace("\\t", " ").Replace("\t", " ");
    cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");
    return cleaned.Trim();
}

static bool IsAssDrawingCommand(string input)
{
    if (string.IsNullOrWhiteSpace(input)) return true;
    
    string cleaned = RemoveMarkup(input);
    
    // Empty after cleaning = garbage
    if (string.IsNullOrWhiteSpace(cleaned)) return true;
    
    // Single character check
    if (cleaned.Length == 1)
    {
        char c = cleaned[0];
        
        // If it's a digit, keep it (e.g. countdown or chapter mark)
        if (char.IsDigit(c)) return false;
        
        // STRICTER FILTER:
        // Even if it is a valid word ("I", "a", "O"), if it has heavy formatting commands like {\an}, 
        // it is likely a sign or particle effect.
        if (input.Contains(@"{\an") || input.Contains(@"\an"))
        {
            return true; // Single letter with alignment tag -> Junk
        }

        // If it has a font face tag, it's likely a sign/karaoke
        if (input.Contains("face=") || input.Contains(@"\fn"))
        {
             return true;
        }

        // Standard valid 1-letter words allowed ONLY if no suspicious tags
        // We remove 'o'/'O' as they are too rare/poetic to risk keeping noise.
        // We keep 'I' and 'a'/'A' (though 'a' is weak).
        // Actually, let's look at the evidence: karaoke uses 'I', 'a', 'o'.
        // Safe bet: Remove ALL single letters that are not digits?
        // Risk: "I" (answer to "Who?"). 
        // Compromise: Keep "I" only if NO tags.
        if (c == 'I') return false; 
        
        // "A" / "a" -> "A?" "A." Very rare. usually "A" is followed by something.
        // "O" -> "O!" Rare.
        
        return true;  // Remove everything else including A, a, O, o, s, t, etc.
    }
    
    // ASS drawing commands check (same as before)
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
        
        return false; // Found a word - not a drawing command
    }
    
    return hasMoveCommand || numberCount >= 2;
}
