using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ReproductionApp
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var filePath = @"e:\openllmvtuber\lingarr-on-steroids\EXAMPLE EPISODES\Jujutsu Kaisen (2020) - S01E20 - Nonstandard [v2 Bluray-1080p Proper][Opus 2.0][x265]-Vodes.eng.ass";
            Console.WriteLine($"Processing {filePath}");
            
            await CleanupSubtitleFile(filePath);
        }

        private static async Task CleanupSubtitleFile(string filePath)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(filePath);
                
                // IMPORTANT: The ExtractionService converts to SRT via ffmpeg FIRST.
                // The input to CleanupSubtitleFile is an SRT file, not the raw ASS.
                // But we only have the .ass file here.
                // WE MUST SIMULATE THE SRT CONVERSION if we want to be accurate, 
                // OR we can try to process the ASS text lines as if they were SRT text lines (ignoring the headers).
                // 
                // However, the user's issue is likely that the "SRT conversion" by ffmpeg produces thousands of lines.
                // Without ffmpeg here, we can't perfectly replicate the input to CleanupSubtitleFile.
                //
                // BUT, looking at the ASS file, the "Dialogue:" lines contain the text.
                // We can parse the ASS Dialogue lines, extract the text, and feed THAT into the cleanup logic 
                // to see if IsAssDrawingCommand or Deduplication fails.
                
                var assLines = lines.Where(l => l.StartsWith("Dialogue:")).ToList();
                Console.WriteLine($"Found {assLines.Count} dialogue lines in ASS file.");

                var output = new List<string>();
                var lastKeptText = string.Empty;
                var keptCount = 0;
                var garbageCount = 0;
                var dedupCount = 0;

                foreach (var line in assLines)
                {
                    // ASS Dialogue format: Dialogue: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
                    // We just want the Text part (everything after the 9th comma)
                    var parts = line.Split(',', 10);
                    if (parts.Length < 10) continue;
                    
                    var rawText = parts[9];
                    
                    // Simulate what ffmpeg produces in SRT:
                    // ffmpeg usually converts {\...} tags to corresponding SRT tags or strips them, 
                    // BUT for complex ASS, it often outputs the raw text with tags stripped or partially kept?
                    // Actually, the logs show the user sees "Batch 1 of 30".
                    // That implies FFmpeg generated 4104 blocks.
                    // 
                    // Let's test the CLEANUP logic on this text.
                    
                    var cleanedText = RemoveMarkup(rawText);
                    
                    if (IsAssDrawingCommand(rawText)) // Note: Original service checks "combinedText" which is the text in the buffer
                    {
                        garbageCount++;
                        // Console.WriteLine($"GARBAGE: {rawText}"); 
                    }
                    else
                    {
                        // Dedupe check
                        var cleanedForDedup = cleanedText; // Simplify for this test
                        
                        if (cleanedForDedup == lastKeptText)
                        {
                            dedupCount++;
                            // Console.WriteLine($"DEDUP: {cleanedText}");
                        }
                        else
                        {
                            keptCount++;
                            lastKeptText = cleanedForDedup;
                            Console.WriteLine($"KEEP: {cleanedText}");
                        }
                    }
                }
                
                Console.WriteLine($"\nResults:");
                Console.WriteLine($"Total Input (Dialogue events): {assLines.Count}");
                Console.WriteLine($"Kept: {keptCount}");
                Console.WriteLine($"Garbage (IsAssDrawingCommand): {garbageCount}");
                Console.WriteLine($"Deduplicated: {dedupCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
        
        // --- COPY OF SERVICE LOGIC ---

        public static string RemoveMarkup(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) {
                return string.Empty;
            }

            // Remove SSA/ASS style tags: {\...}
            string cleaned = Regex.Replace(input, @"\{.*?\}", string.Empty, RegexOptions.Singleline);

            // Remove HTML-style tags: <...>
            cleaned = Regex.Replace(cleaned, @"<.*?>", string.Empty, RegexOptions.Singleline);

            // Replace SSA line breaks
            cleaned = cleaned.Replace("\\N", " ").Replace("\\n", " ");
            cleaned = cleaned.Replace("\\t", " ").Replace("\t", " ");
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

            return cleaned.Trim();
        }

        public static bool IsAssDrawingCommand(string input)
        {
            var cleaned = RemoveMarkup(input);
            if (string.IsNullOrWhiteSpace(cleaned)) return true;

            // Single character filtering - stricter than before
            if (cleaned.Length == 1)
            {
                char c = cleaned[0];
                // Keep 'I' and digits (often used in simple lists or dialogue)
                if (c == 'I' || char.IsDigit(c)) return false; 
                
                // Allow 'a' or 'A' or 'O'? 
                // Some "glitchy" subs use these for frame-by-frame text.
                // If it has complex tags in the original input, it's likely a drawing.
                if (input.Contains(@"{\an") || input.Contains(@"face=") || input.Contains(@"\fn"))
                {
                    return true;
                }
                
                // Otherwise, treat single letters as potential garbage from drawings
                return true; 
            }

            // Heuristic for drawing commands (m, n, l, b coords)
            // e.g. m 0 0 l 100 0 100 100 0 100
             if (Regex.IsMatch(cleaned, @"^[mnlbspc]\s+[\d\.\-\s]+$"))
            {
                return true;
            }

            return false;
        }
    }
}
