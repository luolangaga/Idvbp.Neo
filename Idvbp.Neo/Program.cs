using System;
using System.Threading;
using System.Threading.Tasks;
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
            AppHost.Current.StartAsync(CreateHostBuilder(args))
                .GetAwaiter().GetResult();

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            RunShutdownWithTimeout(TimeSpan.FromSeconds(5));
        }
    }

    private static void RunShutdownWithTimeout(TimeSpan timeout)
    {
        using var doneCts = new CancellationTokenSource();
        var shutdownThread = new Thread(() =>
        {
            try
            {
                AppHost.Current.StopAsync(timeout).GetAwaiter().GetResult();
                AppHost.Current.Dispose();
            }
            catch
            {
            }
            finally
            {
                doneCts.Cancel();
            }
        })
        {
            IsBackground = true,
            Name = "AppHost-Shutdown"
        };
        shutdownThread.Start();

        var completed = doneCts.Token.WaitHandle.WaitOne(timeout + TimeSpan.FromSeconds(5));
        if (!completed)
        {
            Environment.Exit(0);
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
