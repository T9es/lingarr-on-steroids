using Hangfire;
using Lingarr.Core;
using Lingarr.Core.Data;
using Lingarr.Server.Filters;
using Lingarr.Server.Hubs;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Lingarr.Server.Extensions;

public static class ApplicationBuilderExtensions
{
    public static async Task Configure(this WebApplication app)
    {
        app.MapHubs();
        await app.ApplyMigrations();

        // Hangfire Dashboard credentials
        var hangfireUser = Environment.GetEnvironmentVariable("HANGFIRE_USERNAME");
        var hangfirePass = Environment.GetEnvironmentVariable("HANGFIRE_PASSWORD");
        bool credentialsDefaulted = false;

        if (string.IsNullOrEmpty(hangfireUser))
        {
            hangfireUser = "admin";
            credentialsDefaulted = true;
        }

        if (string.IsNullOrEmpty(hangfirePass))
        {
            // Generate a random password if not provided to ensure security
            hangfirePass = Convert.ToBase64String(RandomNumberGenerator.GetBytes(12));
            credentialsDefaulted = true;
        }

        if (credentialsDefaulted)
        {
            Console.WriteLine("################################################################");
            Console.WriteLine("# WARN: Hangfire Dashboard credentials defaulted/generated!    #");
            Console.WriteLine($"# Username: {hangfireUser}");
            Console.WriteLine($"# Password: {hangfirePass}");
            Console.WriteLine("# Please set HANGFIRE_USERNAME and HANGFIRE_PASSWORD env vars. #");
            Console.WriteLine("################################################################");
        }

        // Hangfire Dashboard enabled in all environments for debugging job status
        app.UseHangfireDashboard("/hangfire", new DashboardOptions
        {
            Authorization = [new LingarrAuthorizationFilter(hangfireUser, hangfirePass)]
        });
        
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint($"/swagger/{LingarrVersion.Number}/swagger.json",
                    $"Lingarr HTTP API {LingarrVersion.Number}");
                options.EnableTryItOutByDefault();
            });
        }

        app.UseAuthorization();
        app.MapControllers();
        app.UseStaticFiles();
        app.ConfigureSpa();
    }

    private static void MapHubs(this WebApplication app)
    {
        app.MapHub<TranslationRequestsHub>("/signalr/TranslationRequests");
        app.MapHub<SettingUpdatesHub>("/signalr/SettingUpdates");
        app.MapHub<JobProgressHub>("/signalr/JobProgress");

    }

    private static async Task ApplyMigrations(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<LingarrDbContext>();
            Console.WriteLine("Applying migrations...");
            await context.Database.MigrateAsync();
            Console.WriteLine("Migrations applied successfully.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"An error occurred while applying migrations. {ex}", ex);
        }
    }

    private static void ConfigureSpa(this WebApplication app)
    {
        app.MapWhen(httpContext => 
                httpContext.Request.Path.Value != null && 
                !httpContext.Request.Path.Value.StartsWith("/api") && 
                !httpContext.Request.Path.Value.StartsWith("/signalr"),
            configBuilder =>
            {
                configBuilder.UseSpa(spa =>
                {
                    if (app.Environment.IsDevelopment())
                    {
                        spa.UseProxyToSpaDevelopmentServer("http://Lingarr.Client:9876");
                    }
                });
            });
    }
}
