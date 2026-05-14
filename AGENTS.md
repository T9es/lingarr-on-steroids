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

**Migration Rule:**
- Always create new EF migrations by running `./create-migrations.ps1 -MigrationName "YourMigrationName"` from the repo root.
- Do not hand-create migration `.cs` files or `.Designer.cs` files.
- Do not run `dotnet ef migrations add` separately for only one provider unless the user explicitly asks for a provider-specific migration workflow.
- If a migration is broken or incomplete, first inspect whether it was created outside `create-migrations.ps1` before attempting manual repair.

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

**Branch Policy:**
- Work directly on the current branch by default. In this repository, `main` is the team's test branch and it is expected that implementation work may happen there.
- **Never create or switch to a new branch unless the user explicitly asks you to do so.**
- If a tool, skill, or generic instruction recommends creating a branch, ignore that recommendation unless the user has specifically requested branch creation for this task.

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

### Collaboration Mode (Orchestrator-First)

When the user explicitly requests orchestrator mode, or when handling major feature work, use this workflow:

1. Main Codex agent acts primarily as an orchestrator, not the primary implementer.
2. Delegate deep investigation, substantial code changes, and skeptical review loops to `gpt-5.3-codex` agents running in high-effort mode.
3. Reuse the same skeptical reviewer through the review-fix-review loop until that reviewer explicitly reports the tree clean.
4. Only after the primary skeptical reviewer reports no remaining issues should a **new independent fresh reviewer** be spawned for one final pass when the user asked for extra review depth.
5. Main agent remains accountable for coordination: define scope, assign subtasks, integrate results, accommodate concurrent edits from other workers, run required verification, and communicate status/risks clearly.

### Planning Rules

#### The Rule
A plan is a CONTRACT. If you cannot specify exact file paths, function 
signatures, and data flow, you do not understand the task enough to plan it.

#### Every Plan Must Include:

1. **Change Inventory** — For each file: path, what changes, why, 
   dependencies on other changes.

2. **Data Flow** — Trace from user action to storage. What transforms 
   at each step? What new types/structures are introduced?

3. **Impact & Risk** — What existing code depends on what you're changing? 
   What breaks if applied partially? What do you NOT know? (State unknowns 
   explicitly. Do not proceed past critical unknowns.)

4. **Verification** — Exact commands to validate. Test cases. Manual checks.

#### Reject if:
- File paths are guessed or incomplete
- Edge cases say "handle appropriately" without specifics
- "TBD" or "TODO" appears anywhere
- Plan lacks concrete function/method names

#### Plan Output Depth

Your plan must reflect the FULL depth of your investigation, not a summary.
For each issue or change, include:

1. **What you investigated** — Which files you read, what patterns you found,
   what existing behavior you observed. Show your work.

2. **What you ruled out** — If you considered alternative approaches or root 
   causes, state them and explain why you rejected them. This prevents the 
   implementer from re-investigating the same dead ends.

3. **The exact current state** — Quote or reference the specific lines, 
   function signatures, class names, and variable values that exist RIGHT NOW 
   in the code. Do not describe code from memory — cite what you verified.

4. **Why this fix and not another** — If there are multiple valid approaches, 
   explain your choice. If the user asked you to check something (commits, 
   other components, etc.), show what you found.

A plan that only states WHAT to change without showing HOW you arrived at 
that conclusion is a summary, not a plan. Summaries are rejected.

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

### Progress Tracking

**For complex or multi-step tasks, maintain a progress tracker:**

1. **Create a todo list** at the start of complex tasks (3+ steps or multiple files)
2. **Update progress continuously** - mark items as in_progress when starting, completed when done
3. **Review before committing** - verify no critical items were missed
4. **Only commit after 100% completion** - all items done, all tests passing

**Example workflow:**
```
□ Analyze codebase for hardcoded colors
□ Fix ComponentA.vue
□ Fix ComponentB.vue
□ Run npm run build
□ Verify no type errors
☑ Commit changes
```

**Why this matters:** Models often lose track of progress without explicit tracking. A visible checklist prevents missed steps and incomplete work.

### General Guidelines

1. **Do not guess or assume** - use tools to verify information
2. **Follow existing patterns** - check nearby files for conventions
3. **Maintain type safety** - all TypeScript must be strict-mode compliant
4. **Use async/await** - no synchronous I/O operations in backend
5. **Respect the architecture** - services in Services/, entities in Core/Entities/

### Translation Maintenance

**Translation Files Location:**
- Translation files are stored in `Lingarr.Server/Statics/Translations/`
- Supported languages: English (en), Dutch (nl), German (de), French (fr), Spanish (es), Chinese Simplified (zh), Polish (pl)
- Files are JSON format with 2-space indentation for human readability

**When Adding New Translation Keys:**
1. Add the key to **ALL** translation files (en.json, nl.json, de.json, fr.json, es.json, zh.json, pl.json)
2. Use dot notation for nested keys: `section.subsection.key`
3. Provide meaningful translations - do not use machine translations without review
4. Maintain alphabetical order within each section
5. Test the translation by switching languages in the UI

**Translation Key Naming Convention:**
- Use camelCase for key names: `translateNow`, `sortByTitle`
- Group related keys under common sections: `navigation`, `settings`, `common`
- Use descriptive names that indicate the purpose: `translationTest.startTest`

**Updating TranslationController:**
When adding a new language:
1. Create the translation file: `{language-code}.json`
2. Update `TranslationController.cs` to include the new language in the `GetAvailableLanguages()` method
3. Use native language names in the response: `{ code: "de", name = "Deutsch" }`

**Example Translation Entry:**
```json
{
  "navigation": {
    "dashboard": "Dashboard",
    "movies": "Movies"
  },
  "common": {
    "cancel": "Cancel",
    "confirm": "Confirm"
  }
}
```

**Verification:**
- Ensure all translation files have the same structure and keys
- Check for invalid JSON, duplicate keys, missing frontend-used keys, and suspicious untranslated values by running: `python scripts/verify_translations.py`
- Frontend will log warnings for missing translation keys in the browser console

## Codesight MCP Tools (Repository Mapping)

This project has Codesight MCP tools available for repository navigation. Use them for initial orientation, but **always read actual source files before making behavioral claims or editing**.

**Available tools and their reliability:**

| Tool | Reliable for | Notes |
|---|---|---|
| `codesight_get_summary` | Quick stack/route/component counts, high-impact files | Good first call |
| `codesight_get_wiki_index` | Article listing for navigation | Read before deep-diving |
| `codesight_get_routes` | Route inventory (methods, paths, handler files) | Complete listing |
| `codesight_get_env` | Required env vars | Accurate for static analysis |
| `codesight_get_blast_radius` | Import-based dependency chains | May miss runtime/dynamic deps |
| `codesight_get_schema` | Model names only | **No field detail** — read the actual entity files |
| `codesight_get_wiki_article` | Article index and structure | **Content may be stub/empty** — always verify against source |

**Workflow:**
1. Start with `codesight_get_summary` or `codesight_get_wiki_index` for orientation
2. Read the relevant wiki article via `codesight_get_wiki_article` (~500 tokens)
3. **Read the actual source files** before implementing — the wiki is a navigation aid, not source of truth
4. If Codesight output seems stale, run `npx -y codesight --wiki` from repo root

## Diagnostics

When debugging any kind of issue, check `~/.codex/skills/` for available
diagnostic skills on this machine before asking for manual log access.
These provide SSH access, container inspection, and live troubleshooting
capabilities without manual intervention.

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
