# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Lingarr on Steroids — subtitle translation for Radarr/Sonarr media libraries. A fork of [Lingarr](https://github.com/lingarr-translate/lingarr) with a rebuilt backend, custom translation workers, multi-instance support, and expanded UI.

**Stack:** .NET 9 ASP.NET Core backend, Vue 3 + TypeScript + Tailwind CSS 4 frontend, PostgreSQL (default) or SQLite, Entity Framework Core 9, Hangfire (sync/system jobs), SignalR (real-time updates), custom `BackgroundService` translation workers.

## Build / Test / Run

```bash
# Backend
dotnet restore Lingarr.sln
dotnet build Lingarr.sln --configuration Release

# Tests
dotnet test Lingarr.sln --configuration Release --verbosity normal
dotnet test Lingarr.sln --configuration Release --filter "FullyQualifiedName~YourTestClass"
dotnet test Lingarr.sln --configuration Release --filter "FullyQualifiedName~YourTestClass.YourTestMethod"
dotnet test Lingarr.Server.Tests/Lingarr.Server.Tests.csproj --configuration Release

# Frontend
cd Lingarr.Client && npm ci
npm run dev           # dev server (port 9876)
npm run build         # vue-tsc --noEmit && vite build
npm run format        # prettier ./src --write

# Translation verification
python scripts/verify_translations.py

# Docker dev (Radarr/Sonarr/Postgres/LibreTranslate included)
docker-compose -f docker-compose.dev.yml up -d
# http://localhost:9876 (app), :9877/swagger (API), :9877/hangfire (job dashboard)
```

## Migrations

**Always** use the PowerShell script — do not hand-write migration files:
```bash
./create-migrations.ps1 -MigrationName "YourMigrationName"
```
This creates migrations for PostgreSQL (`Lingarr.Migrations.PostgreSQL/`) and SQLite (`Lingarr.Migrations.SQLite/`) simultaneously. The `create-migrations.ps1` script handles both.

## Architecture

### Startup flow

`Program.cs:1-8` is minimal — delegates to two static extension classes in `Extensions/`:
- **`ServiceCollectionExtensions.Configure(this WebApplicationBuilder)`** — registers all DI services, DbContext, Hangfire (sync workers), SignalR, HttpClient (with Polly retry), Swagger, data protection
- **`ApplicationBuilderExtensions.Configure(this WebApplication)`** — maps SignalR hubs, applies EF migrations on startup (`Database.MigrateAsync()`), configures Hangfire dashboard with auth, configures SPA proxy

### Solution projects

| Project | Role |
|---|---|
| `Lingarr.Server` | ASP.NET Core API (controllers, services, Hubs, Hangfire jobs, translation providers) |
| `Lingarr.Core` | Domain entities, EF Core `LingarrDbContext`, enums, configuration helpers |
| `Lingarr.Migrations.PostgreSQL` | PG EF migrations with design-time factory |
| `Lingarr.Migrations.SQLite` | SQLite EF migrations with design-time factory |
| `Lingarr.Server.Tests` | xUnit + Moq + EF Core InMemory tests |
| `Lingarr.Client` | Vue 3 SPA (Vite, Pinia, Vue Router, Tailwind 4) |

### Database (`Lingarr.Core/Data/LingarrDbContext.cs`)

- 22 DbSets covering movies, shows, seasons, episodes, translation requests/logs, upload batches, statistics, API usage logging, error logs, embedded subtitles, path mappings, custom sources, test results
- Global `DateTime` value converter enforces UTC for all DateTime properties
- Entity configurations via `IEntityTypeConfiguration<T>` classes applied in `OnModelCreating`
- Unique indexes: `TranslationRequest` workload dedupe, `DailyStatistics` on date
- `BaseEntity` abstract class: `Id`, `CreatedAt`, `UpdatedAt` — auto-set in `SaveChangesAsync`
- `DatabaseConfiguration.ConfigureDbContext()` switches between PG and SQLite based on `DB_CONNECTION` env var, uses snake_case naming convention (`EFCore.NamingConventions`)
- Design-time factory at `Lingarr.Core/Data/LingarrDbContextFactory.cs`

### Translation state machine (11 states)

`Lingarr.Core/Enum/TranslationState.cs`:
- `Unknown` (0) — not yet analyzed
- `NotApplicable` (1) — no translation possible/needed
- `Pending` (2) — ready for translation
- `InProgress` (3) — actively being translated
- `Complete` (4) — all translations done
- `Stale` (5) — settings changed, needs re-analysis
- `NoSuitableSubtitles` (6) — sparse tracks only (<50 dialogue entries)
- `Failed` (7) — previous attempt failed
- `AwaitingSource` (8) — waiting for source subtitle
- `OcrPending` (9) — bitmap subtitle needs OCR before translation
- `OcrBlocked` (10) — OCR failed quality gates

State transitions handled by `MediaStateService`.

## Backend structure

```
Lingarr.Server/
├── Controllers/          # 27 API controllers (REST endpoints)
├── Services/
│   ├── Subtitle/         # Parsing (SRT, SSA, VTT), writing, FFmpeg extraction, OCR,
│   │                       integrity/quality validation, orphan cleanup, reconciliation,
│   │                       embedding (MKV), language detection
│   ├── Translation/      # Worker service, cancellation, checkpoints, diagnostics,
│   │   ├── Base/           BaseTranslationService, BaseLanguageService (abstract providers)
│   │   ├── AnthropicService.cs, OpenAiService.cs, DeepSeekService.cs,
│   │   │   GoogleGeminiService.cs, DeepLService.cs, LibreService.cs, ...
│   │   ├── TranslationFactory.cs (service locator/dispatcher)
│   │   ├── ProviderCircuitBreaker.cs (circuit breaker per provider)
│   │   ├── DeferredRepairService.cs (retry failed lines with context)
│   │   ├── BatchFallbackService.cs (graduated retry with chunk splitting)
│   │   ├── PausedTranslationResumeService.cs / PausedTranslationMonitorService.cs
│   │   └── TranslationWorkerService.cs (singleton BackgroundService running parallel workers)
│   ├── Sync/             # Radarr/Sonarr media sync (MovieSync, ShowSync, EpisodeSync, ...)
│   ├── Integration/      # Radarr/Sonarr API integration via HttpClient
│   ├── Cleanup/          # Orphan subtitles, temp files, embedded subtitle caches
│   └── ...               # Automation, Dashboard, Directory, Encryption, Mapping, etc.
├── Providers/            # IntegrationSettingsProvider, InMemoryLogProvider
├── Hubs/                 # 3 SignalR hubs
├── Jobs/                 # 16 Hangfire jobs (SyncMovie, SyncShow, Statistics, Cleanup, ...)
├── Filters/              # JobContextFilter (Hangfire), LingarrAuthorizationFilter
├── Exceptions/           # TranslationException, ProviderPauseException, etc.
├── Interfaces/           # ~60 interfaces matching services
├── Models/               # DTOs, request/response models (Api/, Batch/, Subtitle/, Sync/, Translation/)
└── Statics/Translations/ # 7 language JSON files (en, nl, de, fr, es, zh, pl)
```

### Translation providers

All live in `Services/Translation/`. Two base classes at `Base/`:
- `BaseTranslationService` — common translation flow (rate limiting, prompting, response parsing)
- `BaseLanguageService` — language detection base

Concrete providers: Anthropic, OpenAI, DeepSeek, Google Gemini, DeepL, LibreTranslate, GTranslate (Google/Bing/Yandex/Microsoft via GTranslate library), LocalAI, Chutes.ai, NanoGPT, CrofAI.

`TranslationFactory` resolves the correct provider at runtime. A singleton `ProviderCircuitBreaker` tracks failures per provider and backs off when error thresholds are crossed.

### Translation pipeline (simplified)

1. `TranslationWorkerService` (singleton `BackgroundService`) polls `TranslationRequest` table for pending work
2. Resolves provider via `TranslationFactory`
3. `ITranslationPromptAugmenter` builds prompt enriched with `TranslationPromptContext` (title, season/episode, source type, OCR flag)
4. Sends to AI/API provider with retry via `IBatchFallbackService`
5. Failed lines collected by `IDeferredRepairService`, re-sent with surrounding context after batch completes
6. Progress reported via `TranslationRequestsHub` (SignalR)
7. State updated via `MediaStateService`

### Hangfire vs Custom workers

- **Hangfire** handles: media sync from Radarr/Sonarr, cleanup, statistics, webhooks, integrity checks, bulk operations, OCR detection (queues: `movies`, `shows`, `system`, `webhook`, `default`)
- **Custom workers** (`TranslationWorkerService`) handle: actual subtitle translation jobs (independent Hangfire queue, configurable parallelism via `MAX_PARALLEL_TRANSLATIONS` env var)

## Frontend structure

```
Lingarr.Client/src/
├── pages/                        # Route views (6 top-level)
│   ├── DashboardPage.vue
│   ├── MoviePage.vue
│   ├── ShowPage.vue
│   ├── TranslationPage.vue
│   ├── TranslationTestPage.vue
│   ├── SettingPage.vue           # Parent route with 9 child routes
│   │   ├── settings/ServicesPage.vue
│   │   ├── settings/IntegrationPage.vue
│   │   ├── settings/SubtitlePage.vue
│   │   ├── settings/IntegrityCheckPage.vue
│   │   ├── settings/AutomationPage.vue
│   │   ├── settings/MappingPage.vue
│   │   ├── settings/CustomSourcesPage.vue
│   │   ├── settings/UploadWorkspacePage.vue
│   │   ├── settings/SchedulePage.vue
│   │   └── settings/LogsPage.vue
│   └── HelpPage.vue              # With help/onboarding, help/about children
├── components/
│   ├── common/                   # Badge, Button, Card, Modal, Input, Dropdown, etc. (30+)
│   ├── features/                 # dashboard/, onboarding/, settings/, show/, translation-compare/
│   └── layout/                   # AsideNavigation, PageLayout, ContextMenu
├── store/                        # Pinia stores (instance, mapping, movie, show, translate, etc.)
├── composables/                  # useSignalR, useDashboardSignalR, useDashboardLayout, useDebounce, etc.
├── services/                     # Axios API clients
├── plugins/i18n.ts               # i18n with lazy-loaded translation files
├── router/index.ts               # Vue Router config
├── config/                       # App config
├── directives/                   # Custom Vue directives
└── utils/                        # date.ts, string.ts, providerMetadata.ts, uploadQueue.ts
```

### Vue conventions
- `<script setup lang="ts">` composition API
- Props typed via `interface Props` with `withDefaults(defineProps<Props>(), {...})`
- `@/` path alias for all imports
- Tailwind theme variables: `bg-primary`, `text-primary-content`, `text-accent`, `border-accent`, `bg-secondary` — never hardcoded `text-gray-400` or `bg-black/30`
- Semicolons: `no`, Quotes: single, Indent: 4 spaces, Print width: 100

### SignalR hubs

| Hub | Path | Events |
|---|---|---|
| `TranslationRequestsHub` | `/signalr/TranslationRequests` | `RequestProgress`, `RequestActive` |
| `SettingUpdatesHub` | `/signalr/SettingUpdates` | Setting change notifications |
| `JobProgressHub` | `/signalr/JobProgress` | Background job progress |

Frontend composables: `useSignalR`, `useDashboardSignalR` handle connection lifecycle.

## Codesight MCP (Repository Navigation)

Codesight MCP tools are available for fast repo orientation. These are a **navigation aid** — always read actual source files before editing or making behavioral claims.

- `codesight_get_summary` — quick stack/route/component/env overview (first call)
- `codesight_get_routes` — full route inventory (methods, paths, handler files)
- `codesight_get_wiki_index` / `codesight_get_wiki_article` — wiki-based domain orientation
- `codesight_get_schema` — model names only (**no field details** — read entity files)
- `codesight_get_blast_radius` — import-based dependency chains
- `codesight_get_env` — required env vars

If output seems stale, regenerate: `npx -y codesight --wiki` from repo root.

### Codesight ignore files

`.codesight/` is gitignored and dockerignored — never committed to the repository.

## Key conventions

### C# backend
- Constructor injection, `_camelCase` private fields, `private readonly ILogger<T> _logger`
- All I/O async with `CancellationToken`, never `.Result` or `.Wait()`
- Nullable reference types enabled, `required` on entity properties
- `EF.Functions.ILike` for case-insensitive searches, `Include()` + `AsSplitQuery()` for eager loading
- XML docs on public APIs (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)

### Testing (xUnit + Moq)
- Tests mirror `Services/` structure: `Services/Subtitle/` → `Tests/Services/Subtitle/`
- EF Core InMemory for database tests
- Test data helpers likely in `Tests/Data/`

### Translations
- Files in `Lingarr.Server/Statics/Translations/` (en, nl, de, fr, es, zh, pl — 7 files)
- Add new keys to ALL 7 files (same structure, alphabetical within sections)
- Verify with: `python scripts/verify_translations.py`
- Keys use dot notation: `section.subsection.key`

### Git
- Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`
- All verification must pass before commit: `dotnet build + dotnet test` (backend), `npm run build` (frontend)
- Work on current branch by default — never create/switch branches without explicit request

### Docker dev environment (`docker-compose.dev.yml`)
- `Lingarr.Server` on port 9877 (maps to 9876 internally)
- `Lingarr.Client` on port 9876 (dev server with hot reload)
- `Lingarr.Postgres` (PostgreSQL 16 Alpine)
- `LibreTranslate` (with LT_LOAD_ONLY)
- `radarr` and `sonarr` (Hotio nightly images)
