using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Idvbp.Neo.Core;

public sealed class AppHost : IDisposable
{
    private static readonly Lazy<AppHost> _current = new(() => new AppHost());

    private readonly CancellationTokenSource _shutdownCts = new();
    private IHost? _host;
    private bool _disposed;

    public static AppHost Current => _current.Value;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("AppHost has not been started. Call StartAsync first.");

    public CancellationToken ShutdownToken => _shutdownCts.Token;

    public T GetRequiredService<T>() where T : notnull
        => Services.GetRequiredService<T>();

    public async Task StartAsync(IHostBuilder hostBuilder)
    {
        _host = hostBuilder.Build();
        await _host.StartAsync(_shutdownCts.Token);
    }

    public async Task StopAsync(TimeSpan timeout)
    {
        if (_host is null)
        {
            return;
        }

        _shutdownCts.Cancel();

        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
            await _host.StopAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host?.Dispose();
        _shutdownCts.Dispose();
    }
}
