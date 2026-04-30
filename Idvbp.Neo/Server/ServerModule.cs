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
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

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
        var wwwrootPath = ResolveWwwrootPath();

        services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 1024L * 1024L * 1024L;
            options.ValueLengthLimit = int.MaxValue;
            options.MultipartHeadersLengthLimit = int.MaxValue;
        });
        services.Configure<KestrelServerOptions>(options =>
        {
            options.Limits.MaxRequestBodySize = 1024L * 1024L * 1024L;
        });

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
        services.AddSingleton<IFrontendPackageService>(_ => new FrontendPackageService(wwwrootPath));
        services.AddSingleton<IOfficialCharacterModelService>(sp =>
            new OfficialCharacterModelService(wwwrootPath, sp.GetRequiredService<IResourceCatalogService>()));
        services.AddSingleton<ICharacterModel3DAssetService>(_ => new CharacterModel3DAssetService(wwwrootPath));

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

        var proxiesPath = ReverseProxyConfigLoader.ResolveConfigPath();

        var wwwrootPath = ResolveWwwrootPath();
        app.Use(async (httpContext, next) =>
        {
            if (HttpMethods.IsGet(httpContext.Request.Method) &&
                (httpContext.Request.Path.Equals("/bp-layout", StringComparison.OrdinalIgnoreCase) ||
                 httpContext.Request.Path.Equals("/bp-layout/", StringComparison.OrdinalIgnoreCase)))
            {
                var indexPath = Path.Combine(wwwrootPath, "runtime", "layout-renderer", "index.html");
                if (File.Exists(indexPath))
                {
                    httpContext.Response.ContentType = "text/html; charset=utf-8";
                    httpContext.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                    httpContext.Response.Headers.Pragma = "no-cache";
                    httpContext.Response.Headers.Expires = "0";
                    await httpContext.Response.SendFileAsync(indexPath, httpContext.RequestAborted);
                    return;
                }
            }

            await next();
        });
        if (File.Exists(proxiesPath))
        {
            var proxyConfig = ReverseProxyConfigLoader.Load(proxiesPath);
            if (proxyConfig.Enabled)
            {
                app.UseMiddleware<ReverseProxyMiddleware>(proxyConfig, wwwrootPath);
            }
        }
        var resourcesPath = Path.Combine(Directory.GetCurrentDirectory(), "Resources");
        if (Directory.Exists(wwwrootPath))
        {
            var contentTypeProvider = new FileExtensionContentTypeProvider();
            contentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
            contentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
            contentTypeProvider.Mappings[".bin"] = "application/octet-stream";
            contentTypeProvider.Mappings[".ktx2"] = "image/ktx2";

            // Serve the packaged frontend when it is present beside the desktop app.
            app.UseDefaultFiles();
            app.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(wwwrootPath),
                RequestPath = "",
                ContentTypeProvider = contentTypeProvider,
                OnPrepareResponse = static context =>
                {
                    context.Context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
                    context.Context.Response.Headers.Pragma = "no-cache";
                    context.Context.Response.Headers.Expires = "0";
                }
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
            endpoints.MapProxyConfigApi();
            endpoints.MapFrontendPackageApi();
            endpoints.MapOfficialCharacterModelApi();
            endpoints.MapResourceApi();
            endpoints.MapGet("/api/health", () => Results.Ok(new { status = "ok", timestamp = DateTime.UtcNow }));
        });
    }

    private static string ResolveWwwrootPath()
    {
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        return Directory.Exists(wwwrootPath)
            ? wwwrootPath
            : Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
    }
}
