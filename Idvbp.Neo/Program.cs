using System;
using Avalonia;
using Idvbp.Neo.Core;
using Idvbp.Neo.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo;

internal sealed class Program

{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppHost.Host = CreateHostBuilder(args).Build();
            AppHost.Host.Start();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            AppHost.Host?.StopAsync().GetAwaiter().GetResult();
            AppHost.Host?.Dispose();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var urls = config["Server:Urls"] ?? "http://localhost:5000";

        return Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, configuration) =>
            {
                configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureServices(App.ConfigureServices)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseUrls(urls);
                webBuilder.ConfigureServices(ServerModule.ConfigureServices);
                webBuilder.Configure(ServerModule.Configure);
            });
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
