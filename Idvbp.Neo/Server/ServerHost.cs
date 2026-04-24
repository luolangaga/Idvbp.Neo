using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Idvbp.Neo.Server.Hubs;
using Idvbp.Neo.Server.Middleware;
using Idvbp.Neo.Server.Resources;
using Idvbp.Neo.Server.Services;

namespace Idvbp.Neo.Server;

public class ServerHost
{
    private IHost? _host;
    private readonly string[] _urls;

    public ServerHost(string[] urls)
    {
        _urls = urls;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls(_urls);
                webBuilder.ConfigureServices((context, services) =>
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
                });
                webBuilder.Configure((context, app) =>
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
                        app.UseDefaultFiles();
                        app.UseStaticFiles(new StaticFileOptions
                        {
                            FileProvider = new PhysicalFileProvider(wwwrootPath),
                            RequestPath = ""
                        });
                    }

                    if (Directory.Exists(resourcesPath))
                    {
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
                });
            });

        _host = builder.Build();
        await _host.StartAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_host != null)
        {
            await _host.StopAsync(cancellationToken);
            _host.Dispose();
            _host = null;
        }
    }
}
