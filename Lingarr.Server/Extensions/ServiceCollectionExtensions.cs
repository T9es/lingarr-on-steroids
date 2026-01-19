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
        builder.Services.AddScoped<IOrphanSubtitleCleanupService, OrphanSubtitleCleanupService>();

        // Register translate services
        builder.Services.AddScoped<ITranslationServiceFactory, TranslationFactory>();

        // Added startup service to validate new settings
        builder.Services.AddHostedService<StartupService>();
        
        // Added startup service to clean up orphaned Hangfire locks
        builder.Services.AddHostedService<HangfireLockCleanupService>();

        // Add translation services
        builder.Services.AddTransient<GoogleTranslator>();
        builder.Services.AddTransient<BingTranslator>();
        builder.Services.AddTransient<MicrosoftTranslator>();
        builder.Services.AddTransient<YandexTranslator>();
        builder.Services.AddTransient<OpenAiService>();

        builder.Services.AddTransient<PathConversionService>();
        builder.Services.AddScoped<IStatisticsService, StatisticsService>();
        builder.Services.AddScoped<IChutesUsageService, ChutesUsageService>();
        
        // Translation worker service (singleton BackgroundService that manages translation workers)
        builder.Services.AddSingleton<ITranslationWorkerService, TranslationWorkerService>();
        builder.Services.AddHostedService(sp => (TranslationWorkerService)sp.GetRequiredService<ITranslationWorkerService>());
        
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
        
        // Translation job (scoped to match all its dependencies like LingarrDbContext)
        // This was previously only instantiated by Hangfire, but now TranslationWorkerService needs to resolve it
        builder.Services.AddScoped<Jobs.TranslationJob>();
    }

    private static void ConfigureSignalR(this WebApplicationBuilder builder)
    {
        builder.Services.AddSignalR();
    }

    private static void ConfigureHangfire(this WebApplicationBuilder builder)
    {
        // Sync server: handles media sync, system jobs, and other background tasks
        // Note: Translation jobs are now handled by TranslationWorkerService (BackgroundService)
        var syncWorkers = int.TryParse(
            Environment.GetEnvironmentVariable("MAX_CONCURRENT_JOBS"), out int maxConcurrent)
            ? maxConcurrent
            : 5;
        
        builder.Services.AddHangfireServer(options =>
        {
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
}
