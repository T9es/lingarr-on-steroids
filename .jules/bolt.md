## 2025-12-22 - CultureInfo Lookup Bottleneck
**Learning:** `CultureInfo.GetCultures(CultureTypes.AllCultures)` returns a large array, and iterating it with LINQ `FirstOrDefault` for every subtitle filename part comparison is heavily CPU intensive (O(N*M)) and generates excessive allocations.
**Action:** Replace linear scan with a static `Dictionary<string, string>` (O(1)) for language code lookups. Benchmark showed ~220x speedup.
