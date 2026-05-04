using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo.Core;

/// <summary>
/// 提供进程级访问桌面应用与内嵌 Web 服务器共用的单一通用主机。
/// </summary>
public static class AppHost
{
    /// <summary>
    /// 获取或设置在程序启动时创建的应用主机。
    /// </summary>
    public static IHost Host { get; set; } = null!;

    /// <summary>
    /// 获取由 <see cref="Host"/> 拥有的根服务提供程序。
    /// </summary>
    public static IServiceProvider Services => Host.Services;

    /// <summary>
    /// 从根应用程序服务提供程序解析必需的服务实例。
    /// </summary>
    /// <typeparam name="T">要解析的服务类型。</typeparam>
    /// <returns>解析后的服务实例。</returns>
    public static T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }
}
