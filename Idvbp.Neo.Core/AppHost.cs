using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo.Core;

/// <summary>
/// Provides process-wide access to the single generic host used by the desktop app and embedded web server.
/// </summary>
public static class AppHost
{
    /// <summary>
    /// Gets or sets the application host created during program startup.
    /// </summary>
    public static IHost Host { get; set; } = null!;

    /// <summary>
    /// Gets the root service provider owned by <see cref="Host"/>.
    /// </summary>
    public static IServiceProvider Services => Host.Services;

    /// <summary>
    /// Resolves a required service from the root application service provider.
    /// </summary>
    /// <typeparam name="T">The service type to resolve.</typeparam>
    /// <returns>The resolved service instance.</returns>
    public static T GetRequiredService<T>() where T : notnull
    {
        return Services.GetRequiredService<T>();
    }
}
