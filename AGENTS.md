# Lingarr on Steroids - Agent Guidelines

This document provides essential information for agentic coding agents operating in this repository.

## Project Overview

Lingarr on Steroids is a subtitle translation application for Radarr/Sonarr media libraries. It supports multiple AI translation services (OpenAI, Anthropic, Gemini, DeepSeek, Chutes.ai, LocalAI) and cloud APIs (DeepL, LibreTranslate, Google, Bing, Yandex, Azure).

**Tech Stack:**
- Backend: .NET 9.0, ASP.NET Core, Entity Framework Core, Hangfire, SignalR
- Frontend: Vue 3, TypeScript, Vite, Tailwind CSS 4, Pinia, Vue Router
- Databases: PostgreSQL (default), SQLite (supported)
- Testing: xUnit, Moq, EF Core InMemory

## Build/Lint/Test Commands

### Backend (.NET)

```bash
# Restore dependencies
dotnet restore Lingarr.sln

# Build solution
dotnet build Lingarr.sln --configuration Release

# Run all tests
dotnet test Lingarr.sln --configuration Release --verbosity normal

# Run a single test class
dotnet test Lingarr.sln --configuration Release --filter "FullyQualifiedName~YourTestClass"

# Run a single test method
dotnet test Lingarr.sln --configuration Release --filter "FullyQualifiedName~YourTestClass.YourTestMethod"

# Run tests in specific project
dotnet test Lingarr.Server.Tests/Lingarr.Server.Tests.csproj --configuration Release

# Create migrations (PowerShell)
./create-migrations.ps1 -MigrationName "YourMigrationName"
```

### Frontend (Vue/TypeScript)

```bash
cd Lingarr.Client

# Install dependencies
npm ci

# Development server (hot reload)
npm run dev

# Production build (includes type checking)
npm run build

# Format code with Prettier
npm run format

# Type check only
npx vue-tsc --noEmit

# Lint (if needed)
npx eslint ./src
```

### Docker Development

```bash
# Start development environment
docker-compose -f docker-compose.dev.yml up -d

# Services available:
# - Lingarr: http://localhost:9876
# - Swagger: http://localhost:9877/swagger/index.html
# - Hangfire: http://localhost:9877/hangfire
```

## Code Style Guidelines

### Backend (C#)

**Naming Conventions:**
- PascalCase for public members, classes, methods, properties
- _camelCase for private fields with underscore prefix
- Use meaningful names, avoid abbreviations

**File Organization:**
- One class per file, file name matches class name
- Namespaces follow folder structure: `Lingarr.{Area}.{SubArea}`

**Dependencies & Injection:**
- Use constructor injection for all dependencies
- Store dependencies in private readonly fields: `private readonly ILogger<ClassName> _logger;`

**Async/Await:**
- All I/O operations must be async
- Use `CancellationToken` parameters for long-running operations
- Avoid `.Result` or `.Wait()` - use `await` instead

**Entity Framework:**
- Use `Include()` for eager loading
- Use `AsSplitQuery()` for multiple includes
- Use `ILike` for case-insensitive searches: `EF.Functions.ILike(m.Title, $"%{query}%)`

**Null Safety:**
- Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Use `required` keyword for required properties on entities
- Use `string?` for nullable strings, `int?` for nullable ints
- Check for null before accessing: `if (movie.Path == null) return null;`

**XML Documentation:**
- Enabled (`<GenerateDocumentationFile>true</GenerateDocumentationFile>`)
- Use `/// <summary>` for public APIs
- Use `/// <inheritdoc />` for interface implementations

**Example Service Pattern:**
```csharp
public class MediaService : IMediaService
{
    private readonly LingarrDbContext _dbContext;
    private readonly ILogger<MediaService> _logger;

    public MediaService(
        LingarrDbContext dbContext,
        ILogger<MediaService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<PagedResult<MovieResponse>> GetMovies(...)
    {
        // Implementation
    }
}
```

### Frontend (Vue 3 + TypeScript)

**Naming Conventions:**
- PascalCase for component files: `StatCard.vue`
- camelCase for variables, functions, and props
- Use descriptive names, avoid single-letter variables except in loops

**Component Structure:**
```vue
<template>
    <!-- HTML with Tailwind classes -->
</template>

<script setup lang="ts">
import { useI18n } from '@/plugins/i18n'

// Props with TypeScript interface
interface Props {
    title: string
    total: number
    translated: number
}

const props = withDefaults(defineProps<Props>(), {
    total: 0,
    translated: 0
})

// Composables at top
const { translate } = useI18n()

// Functions
const formatNumber = (num: number): string => {
    return new Intl.NumberFormat().format(num)
}
</script>
```

**Imports:**
- Use `@/` alias for src paths: `import { useI18n } from '@/plugins/i18n'`
- Group imports: Vue core, external libraries, internal modules, types

**TypeScript:**
- `strict` mode enabled - all code must be type-safe
- Define interfaces for props, not inline types
- Use `string | null` for nullable fields, avoid `any`

**Tailwind CSS Theme Variables:**
- `bg-primary`, `bg-secondary`, `bg-tertiary` - backgrounds
- `text-primary-content`, `text-secondary-content` - text colors
- `text-accent`, `bg-accent`, `border-accent` - accent/highlight colors
- Avoid hardcoded colors like `text-gray-400` or `bg-black/30`

**Prettier Formatting:**
- No semicolons
- Single quotes
- 4-space indentation
- Print width: 100 characters
- No trailing commas

**Comments:**
- Do not add comments unless explicitly requested
- Code should be self-documenting through clear naming

## Project Structure

```
Lingarr.sln
├── Lingarr.Server/          # ASP.NET Core backend
│   ├── Controllers/         # API endpoints
│   ├── Services/            # Business logic
│   ├── Interfaces/          # Service interfaces
│   ├── Models/              # DTOs and request/response models
│   └── Hubs/                # SignalR hubs
├── Lingarr.Core/            # Domain entities and interfaces
│   ├── Entities/            # EF Core entities
│   ├── Enum/                # Enumerations
│   ├── Interfaces/           # Domain interfaces
│   └── Data/                # DbContext
├── Lingarr.Migrations.PostgreSQL/
├── Lingarr.Migrations.SQLite/
├── Lingarr.Server.Tests/    # xUnit tests
└── Lingarr.Client/          # Vue 3 frontend
    ├── src/
    │   ├── components/
    │   │   ├── common/      # Reusable UI components
    │   │   ├── features/    # Feature-specific components
    │   │   ├── icons/       # SVG icon components
    │   │   └── layout/      # Layout components
    │   ├── pages/           # Route views
    │   ├── store/           # Pinia stores
    │   ├── plugins/         # Vue plugins (i18n, etc.)
    │   ├── ts/              # TypeScript types and utilities
    │   └── router/          # Vue Router config
    └── package.json
```

## Git Conventions

**Branch Naming:**
- `feat/feature-name` for new features
- `fix/bug-name` for bug fixes

**Commit Messages:**
- Follow Conventional Commits: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`
- Keep messages concise and descriptive
- **All commit messages must be in English**

**Pre-Commit Requirements:**
- All tests must pass: `dotnet test Lingarr.sln --configuration Release`
- Backend must build without errors: `dotnet build Lingarr.sln --configuration Release`
- Frontend must build without errors: `cd Lingarr.Client && npm run build`
- Run linting/formatting before committing

## Key Architectural Patterns

**Multi-Instance Support:**
- Movies/Shows have `SourceInstanceId` for multiple Radarr/Sonarr instances
- Services accept `url` and `apiKey` parameters for instance-specific calls
- Instance configs stored as JSON in settings

**Translation State Machine:**
- `TranslationState` enum: Unknown, Pending, InProgress, Complete, Stale, AwaitingSource, NoSuitableSubtitles, Failed, Interrupted
- `MediaStateService` computes and updates states

**SignalR Real-Time Updates:**
- `/signalr/TranslationRequests` hub for translation progress
- Events: `RequestProgress`, `RequestActive`
- Frontend uses `useDashboardSignalR` composable

**Theme System:**
- CSS variables for theming: primary, secondary, tertiary, accent
- Components must use theme variables, not hardcoded colors

## Agent Instructions

### Mandatory Verification Before Commit

**After ANY code changes, you MUST run ALL applicable verification commands:**

#### Backend Changes
```bash
dotnet build Lingarr.sln --configuration Release
dotnet test Lingarr.sln --configuration Release --verbosity normal
```

#### Frontend Changes
```bash
cd Lingarr.Client && npm run build
```

#### If Both Changed
Run ALL commands above.

### Fix Policy

If verification fails:
1. **ONLY fix issues within your own code changes** made in the current session
2. Do NOT modify unrelated code to fix pre-existing issues
3. If failures are in code you did NOT modify, report them but do not fix
4. Re-run verification after fixes until all pass

### Commit Policy

1. Only commit after ALL verification passes
2. Follow Conventional Commits format: `feat:`, `fix:`, `docs:`, `refactor:`, `test:`, `chore:`
3. Commit messages must be in English
4. **NEVER push changes that contain:**
   - API keys or secrets
   - Hard-coded file paths from user systems
   - Identifiable user information
5. **AGENTS.md can be pushed** - but verify it contains no sensitive information first
6. For all other files: commit locally only, do NOT push unless explicitly requested

### General Guidelines

1. **Do not guess or assume** - use tools to verify information
2. **Follow existing patterns** - check nearby files for conventions
3. **Maintain type safety** - all TypeScript must be strict-mode compliant
4. **Use async/await** - no synchronous I/O operations in backend
5. **Respect the architecture** - services in Services/, entities in Core/Entities/

### Maintaining AGENTS.md

**Update this file when:**
- Build/lint/test commands change
- New frameworks or libraries are added
- Code style conventions evolve
- Project structure changes significantly
- New architectural patterns are introduced

**Default prompt for updating AGENTS.md:**
```
Please analyze this codebase and create an AGENTS.md file containing:
1. Build/lint/test commands - especially for running a single test
2. Code style guidelines including imports, formatting, types, naming conventions, error handling, etc.

The file you create will be given to agentic coding agents (such as yourself) that operate in this repository. Make it about 150 lines long.
If there are Cursor rules (in .cursor/rules/ or .cursorrules) or Copilot rules (in .github/copilot-instructions.md), make sure to include them.
```

**After updating AGENTS.md:**
- Commit the changes with message: `docs: update AGENTS.md with [specific changes]`
- **AGENTS.md can be pushed** - but verify it contains no sensitive information first