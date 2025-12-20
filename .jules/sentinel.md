## 2025-12-20 - Command Injection in Subtitle Extraction
**Vulnerability:** `SubtitleExtractionService` used string interpolation to construct arguments for `Process.Start` when calling FFmpeg/FFprobe. This allowed potential command injection if filenames contained malicious characters (e.g., quotes, spaces).
**Learning:** Using `ProcessStartInfo.Arguments` with string interpolation is risky when handling file paths or user input.
**Prevention:** Always use `ProcessStartInfo.ArgumentList` (introduced in .NET Core 2.1) to pass arguments safely. This handles escaping automatically and prevents injection.
