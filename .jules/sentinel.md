## 2025-12-22 - Unsecured Hangfire Dashboard
**Vulnerability:** The Hangfire Dashboard was exposed without authentication because `LingarrAuthorizationFilter` returned `true` for all requests.
**Learning:** Default authorization filters in scaffolding often permit all access to ease development but become critical risks in production if not updated. The application relies on network isolation rather than application-level auth for most endpoints, but admin dashboards (Hangfire) allow destructive actions and information leakage (job parameters, file paths).
**Prevention:** Always implement `IDashboardAuthorizationFilter` with at least Basic Authentication or restrict to local requests. In this fix, we implemented Basic Auth with support for Environment Variables and safe defaults (randomly generated password) to prevent "default password" attacks.

## 2025-12-23 - Directory Traversal and Hardcoded Secrets
**Vulnerability:** The `DirectoryController` allowed browsing the entire filesystem, including sensitive system directories like `/etc` and `/proc`, without authentication. Additionally, a hardcoded password was found in `appsettings.json`.
**Learning:** Utilities for browsing the filesystem (common in media apps) must be restricted to safe roots or explicitly block system paths. Hardcoded secrets in config files, even if intended for development, are dangerous as they often leak into production images.
**Prevention:** Implemented a blocklist for sensitive system paths in `DirectoryService` to prevent information leakage. Removed the hardcoded secret from configuration.
