using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Idvbp.Neo.Server;
using Microsoft.Extensions.Configuration;

namespace Idvbp.Neo;

sealed class Program
{
    private static ServerHost? _serverHost;

    [STAThread]
    public static void Main(string[] args)
    {
        var cts = new CancellationTokenSource();

        var serverTask = StartServerAsync(cts.Token);

        try
        {
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            cts.Cancel();
            _serverHost?.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static async Task StartServerAsync(CancellationToken cancellationToken)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

        var urls = config["Server:Urls"] ?? "http://localhost:5000";

        _serverHost = new ServerHost([urls]);
        try
        {
            await _serverHost.StartAsync(cancellationToken);
            Console.WriteLine($"[ServerHost] HTTP server started on {urls}");
        }
        catch (OperationCanceledException) { }
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