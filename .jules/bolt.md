## 2025-12-22 - CultureInfo Lookup Bottleneck
**Learning:** `CultureInfo.GetCultures(CultureTypes.AllCultures)` returns a large array, and iterating it with LINQ `FirstOrDefault` for every subtitle filename part comparison is heavily CPU intensive (O(N*M)) and generates excessive allocations.
**Action:** Replace linear scan with a static `Dictionary<string, string>` (O(1)) for language code lookups. Benchmark showed ~220x speedup.

## 2025-12-23 - Over-fetching in Media Lists
**Learning:** `GetShows` was fetching the entire Season/Episode tree for every show in the list, causing massive over-fetching (potentially thousands of entities) for a simple paginated list view.
**Action:** Removed `.Include(s => s.Seasons)` from the `GetShows` endpoint and implemented on-demand fetching via a new `GetShow` endpoint only when the user expands a show.
