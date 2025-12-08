using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GTranslate.Translators;
using Hangfire;
using Hangfire.MySql;
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
        
        // Batch fallback service for graduated retry with chunk splitting
        builder.Services.AddScoped<IBatchFallbackService, BatchFallbackService>();

        // Add Sync services
        builder.Services.AddScoped<IShowSyncService, ShowSyncService>();
        builder.Services.AddScoped<IShowSync, ShowSync>();
        builder.Services.AddScoped<IMovieSyncService, MovieSyncService>();
        builder.Services.AddScoped<IMovieSync, MovieSync>();
        builder.Services.AddScoped<IEpisodeSync, EpisodeSync>();
        builder.Services.AddScoped<ISeasonSync, SeasonSync>();
        builder.Services.AddScoped<IShowSync, ShowSync>();
        builder.Services.AddScoped<IImageSync, ImageSync>();
        
    }

    private static void ConfigureSignalR(this WebApplicationBuilder builder)
    {
        builder.Services.AddSignalR();
    }

    private static void ConfigureHangfire(this WebApplicationBuilder builder)
    {
        var tablePrefix = "_hangfire";
        builder.Services.AddHangfireServer(options =>
        {
            options.Queues = ["movies", "shows", "system", "translation", "default"];
            // Default to 20 workers so the ParallelTranslationLimiter can control actual concurrency
            // The semaphore-based limiter respects the UI setting for max_parallel_translations
            options.WorkerCount = builder.Configuration.GetValue<int?>("MAX_CONCURRENT_JOBS") ?? 20;
        });

        builder.Services.AddHangfire(configuration =>
        {
            configuration
                .UseSimpleAssemblyNameTypeSerializer()
                .UseRecommendedSerializerSettings();

            var dbConnection = builder.Configuration["DbConnection"]?.ToLower() ?? "sqlite";
            if (dbConnection == "mysql")
            {
                var connectionString = builder.Configuration.GetConnectionString("MySqlConnection");
                
                // Check if connection string exists in Configuration (appsettings.json etc)
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    Console.WriteLine("[Hangfire] Connection string not found in Configuration. Falling back to Environment Variables.");
                    var host = Environment.GetEnvironmentVariable("DB_HOST") ?? "Lingarr.Mysql";
                    var port = Environment.GetEnvironmentVariable("DB_PORT") ?? "3306";
                    var db = Environment.GetEnvironmentVariable("DB_DATABASE") ?? "LingarrMysql";
                    var user = Environment.GetEnvironmentVariable("DB_USERNAME") ?? "LingarrMysql";
                    var pass = Environment.GetEnvironmentVariable("DB_PASSWORD") ?? "Secret1234";
                    
                    connectionString = $"Server={host};Port={port};Database={db};Uid={user};Pwd={pass};Allow User Variables=True";
                }
                else 
                {
                    Console.WriteLine("[Hangfire] Connection string FOUND in Configuration.");
                }

                // Debug logging to identify connection issues
                var debugConnectionString = System.Text.RegularExpressions.Regex.Replace(connectionString, "Pwd=.*?;", "Pwd=***;");
                Console.WriteLine($"[Hangfire] Initializing MySQL Storage with connection string: {debugConnectionString}");

                // Retry logic for Hangfire storage initialization
                int maxRetries = 5;
                int delayMs = 2000;
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        var storage = new MySqlStorage(connectionString, new MySqlStorageOptions
                        {
                            TablesPrefix = tablePrefix
                        });
                        configuration.UseStorage(storage);
                        Console.WriteLine("[Hangfire] Successfully verified MySQL connection.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        if (i == maxRetries - 1)
                        {
                            Console.WriteLine($"[Hangfire] Critical Failure: Could not connect to MySQL after {maxRetries} attempts. {ex.Message}");
                            throw;
                        }
                        Console.WriteLine($"[Hangfire] Failed to connect to MySQL (Attempt {i + 1}/{maxRetries}). Retrying in {delayMs}ms... Error: {ex.Message}");
                        System.Threading.Thread.Sleep(delayMs);
                    }
                }
            }
            else
            {
                var sqliteDbPath = builder.Configuration["DB_HANGFIRE_SQLITE_PATH"] ?? "/app/config/Hangfire.db";

                configuration
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseSQLiteStorage(sqliteDbPath, new SQLiteStorageOptions());
            }

            configuration.UseFilter(new JobContextFilter());
        });
    }
}
