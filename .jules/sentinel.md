## 2025-12-25 - Directory Information Leakage
**Vulnerability:** The `DirectoryController` allowed listing hidden directories (e.g., `.ssh`, `.env`) and potentially sensitive system directories if not explicitly blocked.
**Learning:** Utilities that expose filesystem structure can inadvertently leak information about the server environment, even if they don't allow file reading. Hiding dot-files is a standard convention that should be enforced at the API level for such utilities.
**Prevention:** Modified `DirectoryService.GetDirectoryContents` to filter out directories starting with `.`. Also, maintenance of the blocklist is crucial but fragile; allow-listing is preferred where possible.
