# Lingarr on Steroids - Architecture

## Overview

**Lingarr on Steroids** is an advanced subtitle translation application for media libraries, designed as a specialized fork of Lingarr. It integrates with Radarr (movies) and Sonarr (TV shows) to automatically translate subtitle files using various AI and cloud translation services.

The application is built as a full-stack solution with:
- **Backend**: ASP.NET Core 9.0 Web API with Entity Framework Core
- **Frontend**: Vue.js 3 + TypeScript SPA with Vite
- **Database**: PostgreSQL (default) or SQLite support
- **Background Jobs**: Custom TranslationWorkerService + Hangfire for sync tasks

---

## Tech Stack

| Layer | Technology |
|-------|------------|
| Backend | .NET 9.0, ASP.NET Core Web API |
| Frontend | Vue 3, TypeScript, Vite, Tailwind CSS v4 |
| State Management | Pinia |
| Database | PostgreSQL (primary), SQLite (fallback) |
| ORM | Entity Framework Core 9.0 |
| Background Jobs | Custom BackgroundService + Hangfire |
| Real-time | SignalR |
| API Docs | Swagger/OpenAPI |
| Testing | xUnit, Moq |

---

## Directory Structure

```
lingarr-on-steroids/
├── Lingarr.Server/              # ASP.NET Core Web API
│   ├── Controllers/             # API endpoints
│   ├── Services/                # Business logic
│   │   ├── Integration/         # Radarr/Sonarr sync
│   │   ├── Subtitle/            # Subtitle parsing/extraction
│   │   ├── Sync/                # Media synchronization
│   │   └── Translation/         # Translation services
│   ├── Interfaces/              # Service contracts
│   ├── Extensions/              # DI configuration, middleware
│   ├── Filters/                 # Hangfire filters
│   ├── Hubs/                    # SignalR hubs
│   ├── Jobs/                    # Background job definitions
│   ├── Exceptions/              # Custom exceptions
│   └── Providers/               # Configuration providers
├── Lingarr.Core/                # Shared domain models
│   ├── Entities/                # Database entities
│   ├── Configuration/           # EF Core configurations
│   ├── Data/                    # DbContext
│   ├── Enum/                    # Enumerations
│   ├── Interfaces/              # Core abstractions
│   └── Logging/                 # Custom logging
├── Lingarr.Client/              # Vue.js frontend
│   ├── src/
│   │   ├── components/          # Vue components
│   │   │   ├── common/          # Reusable UI components
│   │   │   ├── features/        # Feature-specific components
│   │   │   └── layout/          # Layout components
│   │   ├── pages/               # Page components
│   │   ├── services/            # API clients
│   │   ├── store/               # Pinia stores
│   │   ├── router/              # Vue Router config
│   │   ├── composables/         # Vue composables
│   │   ├── utils/               # Utility functions
│   │   └── plugins/             # Vue plugins
├── Lingarr.Migrations.PostgreSQL/  # PostgreSQL migrations
├── Lingarr.Migrations.SQLite/      # SQLite migrations
├── Lingarr.Server.Tests/        # Backend unit tests
└── docker-compose.dev.yml       # Development environment
```

---

## Core Components

### 1. Translation System

**TranslationWorkerService** (`Services/Translation/TranslationWorkerService.cs`)
- Custom database-driven BackgroundService
- Manages 1-20 concurrent translation workers
- Priority queue support with runtime reordering
- Automatic crash recovery and job retry
- Cooperative cancellation support

**Translation Services** (`Services/Translation/`)
- `AnthropicService` - Claude AI integration
- `OpenAiService` - OpenAI GPT integration
- `DeepSeekService` - DeepSeek AI integration
- `GeminiService` - Google Gemini integration
- `ChutesService` - Chutes.ai with quota management
- `LocalAiService` - Self-hosted models (Ollama)
- `BaseTranslationService` - Abstract base for AI services
- `BatchFallbackService` - Graduated retry with chunk splitting
- `DeferredRepairService` - Context-aware retry for failed translations

### 2. Media Integration

**Radarr/Sonarr Integration** (`Services/Integration/`)
- `RadarrService` - Movie library sync
- `SonarrService` - TV show library sync
- `IntegrationService` - Common integration logic
- Sync services poll *arr APIs and update local database

**Media Sync Pipeline** (`Services/Sync/`)
- `MovieSyncService` / `MovieSync` - Movie synchronization
- `ShowSyncService` / `ShowSync` - TV show synchronization
- `EpisodeSync` - Episode-level sync
- `SeasonSync` - Season-level sync
- `ImageSync` - Poster/fanart sync

### 3. Subtitle Processing

**Subtitle Services** (`Services/Subtitle/`)
- `SubtitleExtractionService` - FFmpeg-based embedded subtitle extraction
- `SubtitleIntegrityService` - Validation and repair
- `OrphanSubtitleCleanupService` - Cleanup of orphaned translations
- `SrtParser` / `SrtWriter` - SRT format handling
- `SsaParser` / `SsaWriter` - ASS/SSA format handling with sanitation

**ASS/SSA Sanitation Features:**
- Drawing block removal (`{\p1}...{\p0}`)
- Heuristic detection of drawing commands
- "Poison" content filtering (musical symbols, sound effects, URLs)
- Sparse subtitle detection (<100 entries)

### 4. State Management

**MediaStateService** (`Services/MediaStateService.cs`)
- 9-state TranslationState system:
  - `Unknown`, `Pending`, `InProgress`, `Complete`
  - `Stale`, `AwaitingSource`, `NoSuitableSubtitles`, `Failed`
- Efficient "what needs translation" queries
- Stale detection when settings change

### 5. Real-time Communication

**SignalR Hubs** (`Hubs/`)
- `JobProgressHub` - Job progress updates
- `SettingUpdatesHub` - Settings change notifications
- `TranslationRequestsHub` - Translation request status

---

## Data Flow

### Translation Request Flow

```
1. User/API creates TranslationRequest
   ↓
2. TranslationRequestService queues request
   ↓
3. TranslationWorkerService polls database
   ↓
4. Worker claims job (sets status to InProgress)
   ↓
5. SubtitleExtractionService extracts subtitles (if embedded)
   ↓
6. TranslationService translates content
   - Batch translation for AI services
   - Deferred repair for failed lines
   ↓
7. SubtitleWriter writes translated file
   ↓
8. MediaStateService updates TranslationState
   ↓
9. SignalR notifies clients of completion
```

### Media Sync Flow

```
1. Hangfire scheduled job triggers
   ↓
2. MovieSyncService/ShowSyncService fetches from *arr API
   ↓
3. Sync services update database (Movies, Shows, Episodes)
   ↓
4. MediaStateService evaluates translation needs
   ↓
5. New TranslationRequests created for pending items
   ↓
6. TranslationWorkerService picks up new requests
```

---

## External Integrations

| Service | Purpose | Location |
|---------|---------|----------|
| Radarr | Movie library metadata | `Services/Integration/RadarrService.cs` |
| Sonarr | TV show library metadata | `Services/Integration/SonarrService.cs` |
| PostgreSQL | Primary database | `Lingarr.Migrations.PostgreSQL/` |
| SQLite | Fallback database | `Lingarr.Migrations.SQLite/` |
| LibreTranslate | Self-hosted translation | `Services/Translation/` |
| OpenAI | GPT translation | `Services/Translation/OpenAiService.cs` |
| Anthropic | Claude translation | `Services/Translation/AnthropicService.cs` |
| DeepL | Professional translation | Via GTranslate library |
| SignalR | Real-time updates | `Hubs/` |

---

## Configuration

### Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DB_CONNECTION` | Database type (`postgresql`/`sqlite`) | `postgresql` |
| `DB_HOST` | PostgreSQL hostname | - |
| `DB_PORT` | PostgreSQL port | `5432` |
| `DB_DATABASE` | Database name | - |
| `DB_USERNAME` | Database username | - |
| `DB_PASSWORD` | Database password | - |
| `MAX_PARALLEL_TRANSLATIONS` | Concurrent translation workers | `4` |
| `MAX_CONCURRENT_JOBS` | Hangfire sync workers | `5` |
| `RADARR_URL` / `RADARR_API_KEY` | Radarr integration | - |
| `SONARR_URL` / `SONARR_API_KEY` | Sonarr integration | - |
| `SERVICE_TYPE` | Translation service | - |
| `AI_PROMPT` | Custom translation prompt | - |

See `Settings.MD` for complete configuration reference.

---

## Build & Deploy

### Development

```bash
# Start development environment
docker-compose -f docker-compose.dev.yml up

# Backend only
dotnet run --project Lingarr.Server

# Frontend only
cd Lingarr.Client && npm run dev
```

### Production

```bash
# Build solution
dotnet build Lingarr.sln --configuration Release

# Run tests
dotnet test Lingarr.sln

# Docker build
docker build -f Lingarr.Server/Dockerfile -t lingarr .
```

### CI/CD

GitHub Actions workflow (`.github/workflows/ci.yml`):
- Backend: .NET 9.0 build + xUnit tests
- Frontend: Node.js 24 build + TypeScript check

---

## Key Architectural Decisions

1. **Custom TranslationWorkerService** - Replaced Hangfire for translations to eliminate queue starvation and enable priority queues
2. **PostgreSQL by Default** - MVCC eliminates lock contention during parallel processing
3. **TranslationState Tracking** - 9-state system enables efficient queries without redundant scanning
4. **Deferred Repair** - Failed translations retried with context at batch end for better LLM performance
5. **SignalR for Real-time** - Live progress updates and settings synchronization
