# Lingarr on Steroids - AI Agent Documentation

This file documents the `Lingarr` project for AI agents. It describes the tech stack, project structure, build commands, and coding conventions.

## 1. Project Overview
Lingarr on Steroids is an advanced subtitling and translation tool that integrates with media servers like Radarr and Sonarr. It uses LLMs and other translation services to generate subtitles.

- **Type**: Monorepo (Client + Server)
- **Primary Languages**: C# (Backend), TypeScript (Frontend)

### 1.1 Key Features & Recent Updates
- **Parallel Translation**: Implemented using `SemaphoreSlim` in `TranslationJob`. Configurable via `MAX_CONCURRENT_JOBS` or settings.
- **Chutes.ai Integration**: Dedicated service `ChutesService` for cost-effective AI translation. Includes usage tracking and rate-limiting buffers.
- **Smart Scheduling**: Automation pages now use a simplified scheduler component instead of raw cron expressions.
- **Resilient Batching**: `BatchFallbackService` handles partial failures in translation batches with graduated retries (Full -> Split -> Itemized).

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
- **Verification**: All changes MUST pass the Docker build and any existing tests.
  - Run `docker-compose -f docker-compose.dev.yml build` to verify the build.
  - Run `dotnet test` to verify backend tests.
