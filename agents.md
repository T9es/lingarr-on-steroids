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