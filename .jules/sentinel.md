## 2025-12-20 - Process Argument Injection via String Concatenation
**Vulnerability:** Use of `ProcessStartInfo.Arguments` with string concatenation allows for argument injection if input contains quotes or shell metacharacters.
**Learning:** Even with `UseShellExecute = false`, .NET's argument parsing can be tricked if arguments are manually quoted and concatenated.
**Prevention:** Always use `ProcessStartInfo.ArgumentList` (available in .NET Core 2.1+) to pass arguments as a safe list, which handles escaping automatically.

## 2025-12-21 - Stored XSS in Log Viewer
**Vulnerability:** The log viewer used `v-html` to render log messages that underwent custom formatting (color tags) but were not sanitized first. An attacker could inject malicious HTML via log messages.
**Learning:** When using `v-html` for custom formatted text, ALWAYS sanitize the raw input first, then apply your safe formatting on top of the escaped text.
**Prevention:** Use an `escapeHtml` helper on the input string before any replacements that generate HTML tags.
