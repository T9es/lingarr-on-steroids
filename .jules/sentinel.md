## 2025-12-20 - Process Argument Injection via String Concatenation
**Vulnerability:** Use of `ProcessStartInfo.Arguments` with string concatenation allows for argument injection if input contains quotes or shell metacharacters.
**Learning:** Even with `UseShellExecute = false`, .NET's argument parsing can be tricked if arguments are manually quoted and concatenated.
**Prevention:** Always use `ProcessStartInfo.ArgumentList` (available in .NET Core 2.1+) to pass arguments as a safe list, which handles escaping automatically.
