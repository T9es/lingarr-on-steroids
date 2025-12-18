using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTranslate.Translators;
using Hangfire;
using Hangfire.PostgreSql;
using Hangfire.Storage.SQLite;
using Lingarr.Core;
using Lingarr.Core.Configuration;
using Lingarr.Core.Data;
using Lingarr.Core.Logging;
using Lingarr.Server.Filters;
using Lingarr.Server.Interfaces.Providers;
using Lingarr.Server.Interfaces.Services;
using Lingarr.Server.Interfaces.Services.Integration;
using Lingarr.Server.Interfaces.Services.Subtitle;
using Lingarr.Server.Interfaces.Services.Sync;
using Lingarr.Server.Interfaces.Services.Translation;
using Lingarr.Server.Listener;
using Lingarr.Server.Providers;
using Lingarr.Server.Services;
using Lingarr.Server.Services.Integration;
using Lingarr.Server.Services.Subtitle;
using Lingarr.Server.Services.Sync;
using Lingarr.Server.Services.Translation;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

namespace Lingarr.Server.Extensions;

public static class ServiceCollectionExtensions
{
    public static void Configure(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().AddJsonOptions(options =>
        {
            options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

        builder.Services.AddEndpointsApiExplorer();
        ;
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient();

        builder.ConfigureSwagger();
        builder.ConfigureLogging();
        builder.ConfigureDatabase();
        builder.ConfigureProviders();
        builder.ConfigureServices();
        builder.ConfigureSignalR();
        builder.ConfigureHangfire();
    }

    private static void ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc(LingarrVersion.Number, new OpenApiInfo
            {
                Title = "Lingarr HTTP API",
                Version = LingarrVersion.Number,
                Description = "Lingarr HTTP API definition",
                License = new OpenApiLicense
                {
                    Name = "GNU Affero General Public License v3.0",
                    Url = new Uri("https://github.com/lingarr-translate/lingarr/blob/main/LICENSE")
                }
            });
            
            var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            options.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
        });
    }

    private static void ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new CustomLogFormatter(Options.Create(new CustomLogFormatterOptions())));
        #if !DEBUG
        builder.Logging.AddProvider(new InMemoryLoggerProvider());
        #endif
    }

    private static void ConfigureDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.AddDbContext<LingarrDbContext>(options =>
        {
            DatabaseConfiguration.ConfigureDbContext(options);
        });
    }

    private static void ConfigureProviders(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IIntegrationSettingsProvider, IntegrationSettingsProvider>();
    }

    private static void ConfigureServices(this WebApplicationBuilder builder)
    {
        // Register generic Lazy<T> support for breaking circular dependencies
        builder.Services.AddTransient(typeof(Lazy<>), typeof(LazyServiceWrapper<>));
        
        builder.Services.AddScoped<ISettingService, SettingService>();
        builder.Services.AddSingleton<SettingChangedListener>();

        builder.Services.AddHostedService<ScheduleInitializationService>();
        builder.Services.AddSingleton<IScheduleService, ScheduleService>();

        builder.Services.AddScoped<IImageService, ImageService>();
        builder.Services.AddScoped<IIntegrationService, IntegrationService>();
        builder.Services.AddScoped<IMediaService, MediaService>();
        builder.Services.AddScoped<IProgressService, ProgressService>();
        builder.Services.AddScoped<IRadarrService, RadarrService>();
        builder.Services.AddScoped<ISonarrService, SonarrService>();
        builder.Services.AddScoped<ISubtitleService, SubtitleService>();
        builder.Services.AddScoped<ITranslationRequestService, TranslationRequestService>();
        builder.Services.AddScoped<IMediaSubtitleProcessor, MediaSubtitleProcessor>();
        builder.Services.AddScoped<IDirectoryService, DirectoryService>();
        builder.Services.AddScoped<IMappingService, MappingService>();

        // Register subtitle services
        builder.Services.AddScoped<ISubtitleParser, SrtParser>();
        builder.Services.AddScoped<ISubtitleWriter, SrtWriter>();
        builder.Services.AddScoped<ISubtitleWriter, SsaWriter>();
        builder.Services.AddScoped<ISubtitleWriter, SsaWriter>();
        builder.Services.AddScoped<ISubtitleExtractionService, SubtitleExtractionService>();
        builder.Services.AddScoped<ISubtitleIntegrityService, SubtitleIntegrityService>();

        // Register translate services
        builder.Services.AddScoped<ITranslationServiceFactory, TranslationFactory>();

        // Added startup service to validate new settings
        builder.Services.AddHostedService<StartupService>();

        // Add translation services
        builder.Services.AddTransient<GoogleTranslator>();
        builder.Services.AddTransient<BingTranslator>();
        builder.Services.AddTransient<MicrosoftTranslator>();
        builder.Services.AddTransient<YandexTranslator>();
        builder.Services.AddTransient<OpenAiService>();

        builder.Services.AddTransient<PathConversionService>();
        builder.Services.AddScoped<IStatisticsService, StatisticsService>();
        builder.Services.AddScoped<IChutesUsageService, ChutesUsageService>();
        
        // Parallel translation limiter (singleton to maintain state across jobs)
        builder.Services.AddSingleton<IParallelTranslationLimiter, ParallelTranslationLimiter>();
        
        // Translation cancellation service (singleton to allow cancelling running jobs)
        builder.Services.AddSingleton<ITranslationCancellationService, TranslationCancellationService>();
        
        // Batch fallback service for graduated retry with chunk splitting
        builder.Services.AddScoped<IBatchFallbackService, BatchFallbackService>();
        
        // Deferred repair service for collecting failed items and retrying with context at the end
        builder.Services.AddScoped<IDeferredRepairService, DeferredRepairService>();

        // Add Sync services
        builder.Services.AddScoped<IShowSyncService, ShowSyncService>();
        builder.Services.AddScoped<IShowSync, ShowSync>();
        builder.Services.AddScoped<IMovieSyncService, MovieSyncService>();
        builder.Services.AddScoped<IMovieSync, MovieSync>();
        builder.Services.AddScoped<IEpisodeSync, EpisodeSync>();
        builder.Services.AddScoped<ISeasonSync, SeasonSync>();
        builder.Services.AddScoped<IShowSync, ShowSync>();
        builder.Services.AddScoped<IImageSync, ImageSync>();
        
        // Test translation service (scoped to match ISettingService lifetime)
        builder.Services.AddScoped<ITestTranslationService, TestTranslationService>();
        
        // Media state service for intelligent translation automation
        builder.Services.AddScoped<IMediaStateService, MediaStateService>();
        
    }

    private static void ConfigureSignalR(this WebApplicationBuilder builder)
    {
        builder.Services.AddSignalR();
    }

    private static void ConfigureHangfire(this WebApplicationBuilder builder)
    {

        
        // Translation server: dedicated workers for translation jobs only
        // Worker count is read from: database setting -> env var -> default (4)
        // Priority queue is checked first, ensuring priority jobs get processed immediately
        var translationWorkers = GetTranslationWorkerCount();
        
        // Store configured value so we can detect if restart is needed later
        Environment.SetEnvironmentVariable("CONFIGURED_TRANSLATION_WORKERS", translationWorkers.ToString());
        
        Console.WriteLine($"[Hangfire] Translation server configured with {translationWorkers} workers");
        
        builder.Services.AddHangfireServer(options =>
        {
            // Use a unique name for this server instance to prevent collision with the sync server
            // keeping the same hostname:pid prefix so it identifies as the same machine.
            options.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:translation";
            
            // Single queue for all translations - priority ordering handled by ParallelTranslationLimiter at runtime
            options.Queues = ["translation"];
            options.WorkerCount = translationWorkers;
        });
        
        // Sync server: handles media sync, system jobs, and other background tasks
        // High worker count for parallel sync operations
        var syncWorkers = int.TryParse(
            Environment.GetEnvironmentVariable("MAX_CONCURRENT_JOBS"), out int maxConcurrent)
            ? maxConcurrent
            : 5;
        
        builder.Services.AddHangfireServer(options =>
        {
            // Use a unique name for this server instance
            options.ServerName = $"{Environment.MachineName}:{Environment.ProcessId}:sync";
            
            options.Queues = ["movies", "shows", "system", "default"];
            options.WorkerCount = syncWorkers;
        });

        builder.Services.AddHangfire(configuration =>
        {
            configuration
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            var dbConnection = Environment.GetEnvironmentVariable("DB_CONNECTION")?.ToLower() ?? "postgresql";
            if (dbConnection == "sqlite")
            {
                var sqliteDbPath = Environment.GetEnvironmentVariable("DB_HANGFIRE_SQLITE_PATH") ?? "/app/config/Hangfire.db";

                configuration
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSQLiteStorage(sqliteDbPath, new SQLiteStorageOptions
                    {
                        // Increase lock timeout for long-running jobs
                        DistributedLockLifetime = TimeSpan.FromHours(24)
                    });
            }
            else // Default: PostgreSQL
            {
                var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "lingarr-postgres";
                var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
                var database = Environment.GetEnvironmentVariable("DB_DATABASE") ?? "lingarr";
                var username = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "lingarr";
                var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "lingarr";

                var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";

                configuration.UsePostgreSqlStorage(opts => opts
                    .UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
                    {
                        // Increase lock timeout for long-running jobs (e.g., BulkIntegrityCheck, SyncJobs)
                        // Default is 10 minutes which causes premature lock expiration
                        DistributedLockTimeout = TimeSpan.FromHours(24)
                    });
            }

            configuration.UseFilter(new JobContextFilter());
        });
    }
    
    /// <summary>
    /// Reads max_parallel_translations from database at startup.
    /// Fallback chain: database -> MAX_PARALLEL_TRANSLATIONS env var -> default (4)
    /// </summary>
    private static int GetTranslationWorkerCount()
    {
        const int defaultWorkers = 4;
        
        // Try environment variable first (allows override without database)
        if (int.TryParse(Environment.GetEnvironmentVariable("MAX_PARALLEL_TRANSLATIONS"), out int envValue) && envValue > 0)
        {
            return envValue;
        }
        
        // Try reading from database
        try
        {
            var dbConnection = Environment.GetEnvironmentVariable("DB_CONNECTION")?.ToLower() ?? "postgresql";
            string? settingValue = null;
            
            if (dbConnection == "sqlite")
            {
                settingValue = ReadSettingFromSqlite("max_parallel_translations");
            }
            else // Default: PostgreSQL
            {
                settingValue = ReadSettingFromPostgreSql("max_parallel_translations");
            }
            
            if (int.TryParse(settingValue, out int dbValue) && dbValue > 0)
            {
                return dbValue;
            }
        }
        catch (Exception ex)
        {
            // This is expected on first run before migrations create the settings table
            Console.WriteLine($"[Hangfire] Could not read max_parallel_translations from database (table may not exist yet): {ex.Message}. Using default.");
        }
        
        return defaultWorkers;
    }
    
    private static string? ReadSettingFromSqlite(string settingKey)
    {
        var sqliteDbPath = Environment.GetEnvironmentVariable("SQLITE_DB_PATH") ?? "local.db";
        var connectionString = $"Data Source=/app/config/{sqliteDbPath}";
        
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString);
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = @key LIMIT 1";
        command.Parameters.AddWithValue("@key", settingKey);
        
        var result = command.ExecuteScalar();
        return result?.ToString();
    }
    
    private static string? ReadSettingFromPostgreSql(string settingKey)
    {
        var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "lingarr-postgres";
        var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
        var database = Environment.GetEnvironmentVariable("DB_DATABASE") ?? "lingarr";
        var username = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "lingarr";
        var password = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "lingarr";
        
        var connectionString = $"Host={host};Port={port};Database={database};Username={username};Password={password}";
        
        using var connection = new Npgsql.NpgsqlConnection(connectionString);
        connection.Open();
        
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = @key LIMIT 1";
        command.Parameters.AddWithValue("@key", settingKey);
        
        var result = command.ExecuteScalar();
        return result?.ToString();
    }
}
