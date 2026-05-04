using System;
using Avalonia;
using Idvbp.Neo.Core;
using Idvbp.Neo.Server;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo;

/// <summary>
/// 应用程序入口点，负责初始化 Avalonia 桌面应用并启动内嵌的 ASP.NET Core 服务器。
/// </summary>
internal sealed class Program
{
    /// <summary>
    /// 程序主入口方法。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 构建并启动通用主机（包含 Web 服务器）
            AppHost.Host = CreateHostBuilder(args).Build();
            AppHost.Host.Start();

            // 启动 Avalonia 桌面应用生命周期
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // 确保应用退出时正确停止并释放主机资源
            AppHost.Host?.StopAsync().GetAwaiter().GetResult();
            AppHost.Host?.Dispose();
        }
    }

    /// <summary>
    /// 创建并配置应用程序主机构建器。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>配置好的主机构建器。</returns>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        // 从 appsettings.json 读取服务器监听地址
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

    /// <summary>
    /// 构建 Avalonia 应用实例，配置平台检测、字体和调试工具。
    /// </summary>
    /// <returns>Avalonia 应用构建器。</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
