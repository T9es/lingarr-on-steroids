# Lingarr on Steroids - AI Agent Documentation

This file documents the `Lingarr` project for AI agents. It describes the tech stack, project structure, build commands, and coding conventions.

## 1. Project Overview
Lingarr on Steroids is an advanced subtitling and translation tool that integrates with media servers like Radarr and Sonarr. It uses LLMs and other translation services to generate subtitles.

- **Type**: Monorepo (Client + Server)
- **Primary Languages**: C# (Backend), TypeScript (Frontend)


## 2. Technology Stack

### Backend (`Lingarr.Server`)
- **Framework**: ASP.NET Core (.NET 9.0)
- **Database**: Entity Framework Core (SQLite / MySQL)
- **Background Jobs**: Hangfire (In-memory, SQLite, or MySQL storage)
- **Real-time Communication**: SignalR
- **Documentation**: Swagger / OpenAPI
- **Testing**: xUnit (`Lingarr.Server.Tests`)

### Frontend (`Lingarr.Client`)
- **Framework**: Vue 3 (Composition API, `<script setup>`)
- **Build Tool**: Vite
- **Language**: TypeScript
- **Styling**: Tailwind CSS v4, PostCSS
- **State Management**: Pinia
- **Routing**: Vue Router
- **HTTP Client**: Axios + SignalR Client

## 3. Project Structure

```
/
├── Lingarr.Client/       # Vue 3 Frontend
│   ├── src/
│   │   ├── components/   # Vue components
│   │   ├── stores/       # Pinia stores
│   │   ├── views/        # Page views
│   │   └── services/     # API clients
│   └── vite.config.ts    # Vite configuration
│
├── Lingarr.Server/       # ASP.NET Core Backend API
│   ├── Controllers/      # API Controllers
│   ├── Services/         # Business Logic (Translation, Sync, etc.)
│   ├── Models/           # VM / DTOs
│   ├── Extensions/       # DI and App Configuration
│   └── Hubs/             # SignalR Hubs
│
├── Lingarr.Core/         # Shared Core Logic (Domain, Data, Logging)
├── Lingarr.Server.Tests/ # Unit Tests (xUnit)
└── Lingarr.Migrations.*/ # EF Core Migrations (Do not edit manually)
```

## 4. Development Commands

### Backend
Run from the root or `Lingarr.Server` directory:

```bash
# Restore dependencies
dotnet restore

# Run the server
dotnet run --project Lingarr.Server

# Run tests
dotnet test
```

### Frontend
Run from the `Lingarr.Client` directory:

```bash
# Install dependencies
npm install

# Start development server
npm run dev

# Build for production
npm run build

# Lint/Format
npm run format
```

## 5. Coding Conventions & Best Practices

### General
- **Formatting**: Adhere to the existing `.editorconfig` and Prettier settings.
- **Async/Await**: Use asynchronous patterns (`async`/`await`) everywhere, especially for I/O and database operations.

### C# (Backend)

#### Service Implementation
Services should follow the interface-implementation pattern and use DI.
Example (`SettingService`):
```csharp
public class SettingService : ISettingService
{
    private readonly LingarrDbContext _dbContext;
    private readonly IMemoryCache _cache;

    public SettingService(LingarrDbContext dbContext, IMemoryCache memoryCache)
    {
        _dbContext = dbContext;
        _cache = memoryCache;
    }

    public async Task<string?> GetSetting(string key)
    {
        // ... caching logic ...
        return await _dbContext.Settings.FirstOrDefaultAsync(s => s.Key == key);
    }
}
```

#### Settings Management
Use `ISettingService` to read/write settings. Do not access `LingarrDbContext.Settings` directly in other services.
- Settings are cached (30min sliding, 1hr absolute).
- `SettingChangedListener` propagates updates.

#### Testing Patterns (xUnit + Moq)
When writing tests:
1. Use `DbContextOptionsBuilder<LingarrDbContext>().UseInMemoryDatabase(...)` for DB mocking.
2. Use `Moq` for `Mock<IService>`.
3. Follow Arrange-Act-Assert.

Example:
```csharp
[Fact]
public async Task MyTest()
{
    // Arrange
    var options = new DbContextOptionsBuilder<LingarrDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options;
    await using var context = new LingarrDbContext(options);
    var mockService = new Mock<ISomeService>();

    // Act
    var service = new MyService(context, mockService.Object);
    await service.DoWork();

    // Assert
    mockService.Verify(x => x.Called(), Times.Once);
}
```

### TypeScript / Vue (Frontend)

#### Component Style
Use **Composition API** with `<script setup lang="ts">`.
Define props using `defineProps<{ ... }>()`.

Example (`CardComponent.vue`):
```vue
<template>
    <div class="rounded-md p-6 shadow-md">
        <h2>{{ title }}</h2>
        <slot name="content"></slot>
    </div>
</template>

<script setup lang="ts">
const { title } = defineProps<{
    title: string
}>()
</script>
```

#### Styling
Use **Tailwind CSS** utility classes.
- Primary colors: `bg-primary`, `text-primary-content`
- Rounded: `rounded-md`
- Spacing: `p-6`, `mb-2`, `space-y-4`

## 6. Boundaries & Rules
- **Configuration**: Do NOT commit `appsettings.Development.json` or any file containing real implementation secrets.
- **Generated Files**: Do not manually edit build artifacts or migration snapshots if possible.
- **Dependencies**: Do not add new heavy NuGet or npm packages without a clear reason.
- **EF Migrations**: Always use generic steps to apply migrations.
  - To create: `../create-migrations.ps1 "MigrationName"` (powershell script helper)
  - **IMPORTANT**: If creating migrations manually or if the script fails, you **MUST** ensure the `*.Designer.cs` file is created and includes the `[Migration("...")]` attribute. Without this file, Entity Framework will **silently ignore** the migration.

## 7. Migration Pitfall: `settings` Table

### The Problem
The `Setting` entity uses a non-standard configuration:
- Uses `Key` as the primary key (not `Id`)
- Does not inherit from `BaseEntity`
- Has no explicit `ToTable()` configuration

When using `InsertData()`, `UpdateData()`, or `DeleteData()` on the `settings` table in migrations, EF Core attempts to resolve the entity mapping at runtime. This **fails on fresh databases** with the error:
```
System.InvalidOperationException: There is no entity type mapped to the table 'settings' which is used in a data operation.
```

### The Solution
**Always use raw SQL for data operations on the `settings` table:**

```csharp
// ❌ DON'T DO THIS - Will fail on fresh databases
migrationBuilder.InsertData(
    table: "settings",
    columns: new[] { "key", "value" },
    values: new object[] { "my_setting", "my_value" });

// ✅ DO THIS INSTEAD - Always works
migrationBuilder.Sql(@"
INSERT INTO `settings` (`key`, `value`) VALUES
('my_setting', 'my_value')
ON DUPLICATE KEY UPDATE `value` = VALUES(`value`);
");
```

### Why This Happens
EF Core's `InsertData()` requires a valid model mapping at migration execution time. The `Setting` entity's unconventional setup (string PK, no base class) causes the migration runner to fail to resolve the mapping when applying migrations from scratch.

Raw SQL bypasses this entirely and is:
- More robust (works regardless of model configuration)
- Faster at runtime (no model validation overhead)
- Future-proof (won't break if the model changes)

Before any coding starts, the user must confirm that you can code. If they don't, your task is to investigate deeply the issue or feature they currently want to do or implement. YOU CAN and SHOULD use any mcp tools at your disposal and web search too to ensure you are up to date with the best practices and implementations.

After you are done fixing or implementing a feature, you should remove any trace files, logs etc that you produced during that process, just so you don't leak any information.

Your fixes and implementations should be full. They should not cause other features to break or degrade. Bugfixing doesn't get an option, the features or fixes should be final, so it is very important you dig deep into the code to understand the issue at play, and not find the very first issue and decide that it is the core problem.

## 8. User's Deployment Environment

### Docker Stack
```yaml
services:
  lingarr:
    image: ree0/lingarr-on-steroids:main
    container_name: lingarr
    ports:
      - "6060:8080"
    gpus: all

  lingarr-mysql:
    image: mariadb:10.5
    container_name: lingarr-mysql
    ports:
      - "25599:3306"
```

### Database Access
- **Host**: `192.168.1.13`
- **Port**: `25599`
- **Database**: `lingarr`
- **Username**: `lingarr`
- **Password**: `123456789`
- **Type**: MariaDB 10.5

### Quick MySQL Access Command
```bash
docker run --rm mariadb:10.5 mysql -h 192.168.1.13 -P 25599 -u lingarr -p123456789 lingarr -e "QUERY_HERE"
```

### Application URL
- **Base URL**: `http://192.168.1.13:6060`

# Agent Rules & Instructions

You are a rigorous coding assistant. You DO NOT skim. You DO NOT guess.

---

## 0. First Step: Read Project Context

**Before starting ANY task**, read these files if they exist:
- `AGENTS.md` (this file) – Workflow and behavioral rules.
- `README.md` – Project overview and setup instructions.
- Any `docs/` or instruction files in the project root.

---

## 1. Mandatory Planning Phase (Before ANY Execution)

When tackling ANY task, you MUST first enter **PLAN MODE**.

### Part A: Executive Summary (For Non-Coders)
Provide a clear, jargon-free overview:
- **What** the proposed change accomplishes (in plain English).
- **Why** it's being done and what problem it solves.
- **Impact**: User-facing effects, potential risks, and trade-offs.
- Keep this to 3-5 bullet points.

### Part B: Technical Implementation Plan
Provide full technical details:
- **Files to modify/create**: List each file with its absolute path.
- **Code snippets**: Show key changes (before/after if applicable).
- **Dependencies**: Other components affected, imports to add, etc.
- **Testing**: How you will verify the change works.
- **Rollback**: How to undo if something goes wrong.

### Confidence Level
After planning, state your confidence: **High / Medium / Low**.
- If **Low**, STOP and ask clarifying questions before proceeding.

**STOP and request user approval** before moving to Build/Execute Mode.

---

## 2. Git/PR Workflow

### Branch Targeting
- **Always check if a `dev` branch exists**: `git branch -a | grep dev`
- If `dev` exists, **all PRs must target `dev`**, NOT `main`.
- If `dev` does NOT exist, fall back to `main`.

### PR Descriptions
Every PR description MUST include:
1. **Plain-English Summary**: What this PR does, explained to a non-coder.
2. **Why**: The problem being solved and why this approach was chosen.
3. **What Changed**: Bullet list of files/components modified.
4. **How to Test**: Steps to verify the change works.
5. **Rollback Plan**: How to undo if something breaks.

---

## 3. Mandatory MCP Tool Usage

Before answering ANY question about the codebase, you MUST use the available MCP tools to verify your assumptions:
- **`code-pathfinder`**: Understand project structure, call graphs, and relationships.
- **`mgrep`**: Find specific string occurrences across the project.
- **`owlex`**: Request a second opinion on architecture from a council of AI models.
- **`vitest`**: Run and analyze tests after making changes.
- **`context7`**: Search library documentation when uncertain about an API.

**Never assume. Always verify.**

---

## 4. Change Size Limits

- If a change touches **more than 5 files**, break it into smaller tasks.
- Request approval for each chunk separately.
- Never submit massive PRs without explicit user permission.

---

## 5. Testing Mandate

After ANY code change:
1. Run `npm run build` (or equivalent build command).
2. Run `npm run test` (or equivalent test command).
3. If tests fail, diagnose and fix BEFORE reporting completion.

---

## 6. Anti-Hallucination Rules

- Never assume a file exists. Check it.
- Never assume a function signature. Read it.
- If you are unsure, SEARCH first.
- If some implementation requires internet research, do it.
- All claims you present must be vetted by the real code.

---

## 7. Lessons Learned

If you encounter a persistent error requiring multiple fix attempts:
1. Analyze why the first attempt failed.
2. Abstract the specific error into a general rule.
3. Document the lesson learned to prevent future occurrences.
