# Lingarr on Steroids - Code Style Guide

This document describes the coding conventions and patterns used in this codebase.

---

## Naming Conventions

### C# Backend

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `TranslationWorkerService` |
| Interfaces | PascalCase with `I` prefix | `ITranslationService` |
| Methods | PascalCase | `ProcessTranslationAsync` |
| Properties | PascalCase | `MaxWorkers` |
| Fields (private) | `_camelCase` | `_logger`, `_serviceProvider` |
| Constants | PascalCase | `MaxWorkersLimit` |
| Enums | PascalCase | `TranslationState` |
| Enum values | PascalCase | `InProgress`, `NoSuitableSubtitles` |
| Generic type params | PascalCase with `T` prefix | `TService` |
| Files | Match class name | `TranslationWorkerService.cs` |
| Namespaces | PascalCase, company.product | `Lingarr.Server.Services` |

### Vue/TypeScript Frontend

| Element | Convention | Example |
|---------|------------|---------|
| Components | PascalCase | `BadgeComponent.vue` |
| Composables | camelCase with `use` prefix | `useTranslationRequest` |
| Stores | camelCase with `use` prefix + Store | `useSettingStore` |
| Types/Interfaces | PascalCase | `TranslationRequest` |
| Variables | camelCase | `activeRequests` |
| Constants | UPPER_SNAKE_CASE | `API_BASE_URL` |
| Files (components) | PascalCase | `TranslationProgress.vue` |
| Files (composables) | camelCase | `useSettings.ts` |

---

## File Organization

### C# Project Structure

```
ProjectName/
├── Controllers/          # API controllers only
├── Services/             # Business logic
│   ├── FeatureArea/      # Group related services
│   └── Base/             # Abstract base classes
├── Interfaces/           # Service contracts
│   └── Services/         # Mirror service structure
├── Entities/             # Domain models (in Core)
├── Configuration/        # EF Core configurations
├── Extensions/           # DI and middleware extensions
├── Filters/              # Action/Hangfire filters
├── Hubs/                 # SignalR hubs
├── Jobs/                 # Background job classes
├── Exceptions/           # Custom exceptions
└── Providers/            # Configuration providers
```

### Vue Project Structure

```
src/
├── components/
│   ├── common/           # Reusable UI components
│   ├── features/         # Feature-specific components
│   │   ├── dashboard/
│   │   ├── settings/
│   │   └── movies/
│   └── layout/           # Layout components
├── pages/                # Route-level components
├── services/             # API client classes
├── store/                # Pinia stores
├── router/               # Route definitions
├── composables/          # Reusable composition functions
├── utils/                # Helper functions
├── plugins/              # Vue plugins
├── assets/               # Static assets
└── directives/           # Custom Vue directives
```

---

## Import Style

### C# Usings

```csharp
// System namespaces first
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Third-party libraries
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Hangfire;

// Internal namespaces (ordered by project)
using Lingarr.Core.Entities;
using Lingarr.Core.Enum;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Services;
```

### TypeScript/Vue Imports

```typescript
// Vue core
import { ref, computed, onMounted } from 'vue'
import { defineComponent } from 'vue'

// Third-party libraries
import axios from 'axios'
import { useRoute } from 'vue-router'

// Internal absolute imports (@/ alias)
import { useSettingStore } from '@/store/setting'
import { useTranslationRequestStore } from '@/store/translationRequest'
import type { TranslationRequest } from '@/ts/interfaces'

// Relative imports (same directory only)
import { helperFunction } from './utils'
```

---

## Code Patterns

### C# Patterns

**Async/Await**
```csharp
// Always use async suffix for async methods
public async Task<TranslationResult> TranslateAsync(
    string text, 
    CancellationToken cancellationToken = default)
{
    // Pass cancellation token through
    var result = await _httpClient.GetAsync(url, cancellationToken);
    return await ParseResultAsync(result, cancellationToken);
}
```

**Dependency Injection**
```csharp
// Constructor injection preferred
public class TranslationService : ITranslationService
{
    private readonly ILogger<TranslationService> _logger;
    private readonly ISettingService _settingService;

    public TranslationService(
        ILogger<TranslationService> logger,
        ISettingService settingService)
    {
        _logger = logger;
        _settingService = settingService;
    }
}
```

**Entity Framework**
```csharp
// Use IQueryable for composable queries
public async Task<List<Movie>> GetPendingMoviesAsync()
{
    return await _dbContext.Movies
        .Where(m => m.TranslationState == TranslationState.Pending)
        .OrderBy(m => m.PriorityDate)
        .ToListAsync();
}
```

**BackgroundService Pattern**
```csharp
public class TranslationWorkerService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DoWorkAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Worker error");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }
}
```

### Vue/TypeScript Patterns

**Composition API**
```typescript
<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useSettingStore } from '@/store/setting'

// Reactive state
const isLoading = ref(false)
const items = ref<TranslationRequest[]>([])

// Computed
const pendingCount = computed(() => 
    items.value.filter(i => i.status === 'pending').length
)

// Methods
async function loadData() {
    isLoading.value = true
    try {
        items.value = await fetchItems()
    } finally {
        isLoading.value = false
    }
}

// Lifecycle
onMounted(() => {
    loadData()
})
</script>
```

**Pinia Store Pattern**
```typescript
import { defineStore } from 'pinia'
import { ref, computed } from 'vue'

export const useSettingStore = defineStore('settings', () => {
    // State
    const settings = ref<Record<string, string>>({})
    const isLoading = ref(false)
    
    // Getters
    const apiUrl = computed(() => settings.value.apiUrl)
    
    // Actions
    async function loadSettings() {
        isLoading.value = true
        settings.value = await fetchSettings()
        isLoading.value = false
    }
    
    return { settings, isLoading, apiUrl, loadSettings }
})
```

---

## Error Handling

### C#

```csharp
// Custom exceptions for domain errors
public class TranslationException : Exception
{
    public TranslationException(string message) : base(message) { }
    public TranslationException(string message, Exception inner) : base(message, inner) { }
}

// Try-catch with specific exception types
try
{
    await _translationService.TranslateAsync(text);
}
catch (TranslationException ex)
{
    _logger.LogWarning(ex, "Translation failed for {Text}", text);
    // Handle specific error
}
catch (Exception ex)
{
    _logger.LogError(ex, "Unexpected error during translation");
    throw; // Re-throw unexpected errors
}
```

### TypeScript

```typescript
// Use try-catch with typed errors
try {
    await api.translate(request)
} catch (error) {
    if (axios.isAxiosError(error)) {
        // Handle API error
        console.error('API Error:', error.response?.data)
    } else {
        // Handle unexpected error
        console.error('Unexpected error:', error)
        throw error
    }
}
```

---

## Logging

### C# Logging

```csharp
// Use structured logging with templates
_logger.LogInformation(
    "Processing translation {TranslationId} for {MediaType}:{MediaId}",
    request.Id, request.MediaType, request.MediaId);

// Log levels appropriately
_logger.LogDebug("Detailed debug info: {Data}", data);           // Development only
_logger.LogInformation("Translation completed");                  // Normal operations
_logger.LogWarning("Translation took longer than expected");      // Issues that don't fail
_logger.LogError(ex, "Translation failed for {Id}", id);          // Errors
```

---

## Testing

### C# Test Pattern (xUnit)

```csharp
public class TranslationServiceTests
{
    private readonly Mock<ISettingService> _settingServiceMock;
    private readonly TranslationService _service;

    public TranslationServiceTests()
    {
        _settingServiceMock = new Mock<ISettingService>();
        _service = new TranslationService(
            Mock.Of<ILogger<TranslationService>>(),
            _settingServiceMock.Object);
    }

    [Fact]
    public async Task TranslateAsync_WithValidText_ReturnsTranslation()
    {
        // Arrange
        var text = "Hello";
        _settingServiceMock.Setup(s => s.GetSetting("api_key"))
            .ReturnsAsync("test-key");

        // Act
        var result = await _service.TranslateAsync(text);

        // Assert
        Assert.NotNull(result);
        Assert.NotEqual(text, result);
    }
}
```

---

## Do's and Don'ts

### Do

- ✅ Use `async/await` consistently - avoid `.Result` or `.Wait()`
- ✅ Pass `CancellationToken` through async call chains
- ✅ Use `required` modifier for mandatory properties in entities
- ✅ Use nullable reference types (`string?` for nullable strings)
- ✅ Add XML documentation to public APIs
- ✅ Use `sealed` on classes not designed for inheritance
- ✅ Prefer `IReadOnlyList<T>` over `List<T>` for return types
- ✅ Use `record` for immutable DTOs
- ✅ Group related services in subdirectories under `Services/`
- ✅ Use `const` for magic numbers/strings

### Don't

- ❌ Don't use `async void` (except for event handlers)
- ❌ Don't catch `Exception` without logging or re-throwing
- ❌ Don't use service locator pattern - use constructor injection
- ❌ Don't put business logic in controllers
- ❌ Don't use `DateTime.Now` - use `DateTime.UtcNow`
- ❌ Don't hardcode connection strings or API keys
- ❌ Don't use `== null` - use `is null` or pattern matching
- ❌ Don't create god classes - keep services focused
- ❌ Don't mix sync and async code arbitrarily

---

## Configuration Files

### ESLint (Vue/TS)

Located at `Lingarr.Client/eslint.config.js`:
- Uses `@typescript-eslint` for TypeScript
- Vue-specific rules via `eslint-plugin-vue`
- Prettier integration for formatting

### Prettier

```json
{
  "semi": false,
  "singleQuote": true,
  "tabWidth": 4,
  "trailingComma": "none"
}
```

### EditorConfig

```
[*]
indent_style = space
indent_size = 4
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true
```

---

## Database Conventions

### Entity Framework

```csharp
// Table names are pluralized
public DbSet<Movie> Movies { get; set; }

// Use fluent configuration for complex mappings
public class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.RadarrId).IsUnique();
        builder.Property(m => m.Title).HasMaxLength(500);
    }
}

// Use UTC for all DateTime
var dateTimeConverter = new ValueConverter<DateTime, DateTime>(
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc),
    v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
```

---

## API Conventions

### REST Endpoints

```csharp
[ApiController]
[Route("api/[controller]")]
public class TranslationController : ControllerBase
{
    // GET api/translation
    [HttpGet]
    public async Task<ActionResult<List<TranslationRequest>>> GetAll()

    // GET api/translation/5
    [HttpGet("{id}")]
    public async Task<ActionResult<TranslationRequest>> Get(int id)

    // POST api/translation
    [HttpPost]
    public async Task<ActionResult<TranslationRequest>> Create(CreateRequest request)

    // DELETE api/translation/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
}
```

### Response Patterns

```csharp
// Return appropriate status codes
return Ok(result);           // 200
return CreatedAtAction(...); // 201
return NoContent();          // 204
return BadRequest(error);    // 400
return NotFound();           // 404
```
