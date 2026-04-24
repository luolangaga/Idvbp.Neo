using System;
using System.IO;
using Idvbp.Neo.Server.Hubs;
using Idvbp.Neo.Server.Middleware;
using Idvbp.Neo.Server.Resources;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo.Server;

/// <summary>
/// Contains ASP.NET Core service and middleware registration for the embedded server.
/// </summary>
public static class ServerModule
{
    /// <summary>
    /// Registers SignalR, room services, resource services, reverse proxy dependencies, and CORS.
    /// </summary>
    /// <param name="context">The web host builder context.</param>
    /// <param name="services">The web host service collection.</param>
    public static void ConfigureServices(WebHostBuilderContext context, IServiceCollection services)
    {
        var databasePath = context.Configuration.GetValue<string>("LiteDb:DatabasePath") ?? "data/idvbp-neo.db";
        var resourcesPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources");

        services.AddSignalR()
            .AddHubOptions<GameHub>(options =>
            {
                options.EnableDetailedErrors = true;
            });

        services.AddSingleton<IRoomRepository>(_ => new LiteDbRoomRepository(databasePath));
        services.AddSingleton<RoomSubscriptionRegistry>();
        services.AddSingleton<IRoomEventPublisher, RoomEventPublisher>();
        services.AddSingleton<IRoomService, RoomService>();
        services.AddSingleton<IResourceCatalogService>(_ => new ResourceCatalogService(resourcesPath));

        services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials()
                    .SetIsOriginAllowed(_ => true);
            });
        });
    }

    /// <summary>
    /// Configures middleware and endpoints for the embedded ASP.NET Core server.
    /// </summary>
    /// <param name="context">The web host builder context.</param>
    /// <param name="app">The ASP.NET Core application builder.</param>
    public static void Configure(WebHostBuilderContext context, IApplicationBuilder app)
    {
        var env = context.HostingEnvironment;

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseCors();

        var useReverseProxy = context.Configuration.GetValue<bool>("Server:UseReverseProxy");
        if (useReverseProxy)
        {
            var proxyTarget = context.Configuration.GetValue<string>("Server:ReverseProxy:TargetUrl")!;
            var proxyPrefix = context.Configuration.GetValue<string>("Server:ReverseProxy:PathPrefix") ?? "/proxy";
            var proxyOptions = new ReverseProxyOptions
            {
                TargetUrl = proxyTarget,
                PathPrefix = proxyPrefix
            };
            app.UseMiddleware<ReverseProxyMiddleware>(proxyOptions);
        }

        var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var resourcesPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources");
        if (Directory.Exists(wwwrootPath))
        {
            // Serve the packaged frontend when it is present beside the desktop app.
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath),
                RequestPath = ""
            });
        }

        if (Directory.Exists(resourcesPath))
        {
            // Expose bundled game resources to browser clients under a stable URL prefix.
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(resourcesPath),
                RequestPath = "/resources"
            });
        }

        app.UseRouting();

        app.UseEndpoints(endpoints =>
        {
            endpoints.MapHub<GameHub>("/hubs/game");
            endpoints.MapBpApi();
            endpoints.MapResourceApi();
            endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
        });
    }
}
