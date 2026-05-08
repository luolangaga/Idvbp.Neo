using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Idvbp.Neo.Server.Services;

public interface IGitHubProxyService
{
    IReadOnlyCollection<GitHubProxyEndpoint> GetEndpoints();
    GitHubProxyEndpoint GetSelectedEndpoint();
    Task SetSelectedEndpointAsync(string id, CancellationToken cancellationToken = default);
    Task<GitHubProxyEndpoint> AddCustomEndpointAsync(string name, string urlTemplate, CancellationToken cancellationToken = default);
    Uri RewriteUri(string url);
    Task<T?> GetFromJsonAsync<T>(string url, CancellationToken cancellationToken = default);
    Task<byte[]> GetByteArrayAsync(string url, CancellationToken cancellationToken = default);
    Task DownloadAsync(string url, Stream output, IProgress<GitHubDownloadProgress>? progress = null, CancellationToken cancellationToken = default);
}

public sealed class GitHubProxyService : IGitHubProxyService
{
    public const string DirectEndpointId = "direct";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly string _defaultsPath;
    private readonly string _settingsPath;
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GitHubProxyDefaults? _defaults;
    private GitHubProxyUserSettings? _settings;

    public GitHubProxyService(string defaultsPath, string settingsPath)
    {
        _defaultsPath = defaultsPath;
        _settingsPath = settingsPath;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Idvbp.Neo");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public IReadOnlyCollection<GitHubProxyEndpoint> GetEndpoints()
    {
        EnsureLoaded();
        return BuildEndpoints().ToArray();
    }

    public GitHubProxyEndpoint GetSelectedEndpoint()
    {
        EnsureLoaded();
        var endpoints = BuildEndpoints().ToArray();
        var selectedId = _settings!.SelectedProxyId;
        return endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Id, selectedId, StringComparison.OrdinalIgnoreCase))
               ?? endpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Id, _defaults!.DefaultProxyId, StringComparison.OrdinalIgnoreCase))
               ?? endpoints[0];
    }

    public async Task SetSelectedEndpointAsync(string id, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureLoadedCore();
            if (!BuildEndpoints().Any(endpoint => string.Equals(endpoint.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                throw new KeyNotFoundException($"GitHub proxy endpoint '{id}' was not found.");
            }

            _settings!.SelectedProxyId = id;
            await SaveSettingsAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GitHubProxyEndpoint> AddCustomEndpointAsync(string name, string urlTemplate, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Proxy name is required.", nameof(name));
        }

        var normalizedTemplate = NormalizeUrlTemplate(urlTemplate);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            EnsureLoadedCore();
            var id = CreateCustomEndpointId(name, BuildEndpoints().Select(x => x.Id));
            var endpoint = new GitHubProxyEndpoint(id, name.Trim(), normalizedTemplate, true, true);
            _settings!.CustomProxies.Add(endpoint);
            _settings.SelectedProxyId = id;
            await SaveSettingsAsync(cancellationToken);
            return endpoint;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Uri RewriteUri(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var original))
        {
            throw new ArgumentException("A valid absolute URL is required.", nameof(url));
        }

        if (!IsGitHubHost(original.Host))
        {
            return original;
        }

        var endpoint = GetSelectedEndpoint();
        if (string.Equals(endpoint.Id, DirectEndpointId, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(endpoint.UrlTemplate))
        {
            return original;
        }

        var template = endpoint.UrlTemplate.Trim();
        var rewritten = template.Contains("{url}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{url}", original.ToString(), StringComparison.OrdinalIgnoreCase)
            : $"{template.TrimEnd('/')}/{original}";
        return new Uri(rewritten, UriKind.Absolute);
    }

    public async Task<T?> GetFromJsonAsync<T>(string url, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<T>(RewriteUri(url), JsonOptions, cancellationToken);
    }

    public async Task<byte[]> GetByteArrayAsync(string url, CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetByteArrayAsync(RewriteUri(url), cancellationToken);
    }

    public async Task DownloadAsync(string url, Stream output, IProgress<GitHubDownloadProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(RewriteUri(url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new byte[1024 * 96];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            received += read;
            progress?.Report(new GitHubDownloadProgress(received, total));
        }
    }

    private void EnsureLoaded()
    {
        _gate.Wait();
        try
        {
            EnsureLoadedCore();
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureLoadedCore()
    {
        _defaults ??= LoadDefaults();
        _settings ??= LoadSettings();
        if (string.IsNullOrWhiteSpace(_settings.SelectedProxyId))
        {
            _settings.SelectedProxyId = string.IsNullOrWhiteSpace(_defaults.DefaultProxyId)
                ? DirectEndpointId
                : _defaults.DefaultProxyId;
        }
    }

    private GitHubProxyDefaults LoadDefaults()
    {
        if (!File.Exists(_defaultsPath))
        {
            return new GitHubProxyDefaults();
        }

        try
        {
            return JsonSerializer.Deserialize<GitHubProxyDefaults>(File.ReadAllText(_defaultsPath), JsonOptions)
                   ?? new GitHubProxyDefaults();
        }
        catch
        {
            return new GitHubProxyDefaults();
        }
    }

    private GitHubProxyUserSettings LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            return new GitHubProxyUserSettings();
        }

        try
        {
            return JsonSerializer.Deserialize<GitHubProxyUserSettings>(File.ReadAllText(_settingsPath), JsonOptions)
                   ?? new GitHubProxyUserSettings();
        }
        catch
        {
            return new GitHubProxyUserSettings();
        }
    }

    private async Task SaveSettingsAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_settingsPath))!);
        await using var stream = File.Create(_settingsPath);
        await JsonSerializer.SerializeAsync(stream, _settings, JsonOptions, cancellationToken);
    }

    private IEnumerable<GitHubProxyEndpoint> BuildEndpoints()
    {
        yield return new GitHubProxyEndpoint(DirectEndpointId, "直连 GitHub", "", true, false);

        foreach (var endpoint in _defaults!.Proxies.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Id)))
        {
            yield return endpoint with { IsCustom = false };
        }

        foreach (var endpoint in _settings!.CustomProxies.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Id)))
        {
            yield return endpoint with { IsCustom = true };
        }
    }

    private static string NormalizeUrlTemplate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Proxy URL is required.", nameof(value));
        }

        var template = value.Trim();
        var probe = template.Contains("{url}", StringComparison.OrdinalIgnoreCase)
            ? template.Replace("{url}", "https://github.com/", StringComparison.OrdinalIgnoreCase)
            : template;
        if (!Uri.TryCreate(probe, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new ArgumentException("Proxy URL must be an absolute http or https URL.", nameof(value));
        }

        return template;
    }

    private static string CreateCustomEndpointId(string name, IEnumerable<string> existingIds)
    {
        var used = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase) { DirectEndpointId };
        var baseId = "custom-" + string.Concat(name.Trim().ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-'))
            .Trim('-');
        if (baseId == "custom-")
        {
            baseId = "custom-proxy";
        }

        var id = baseId;
        var index = 1;
        while (used.Contains(id))
        {
            id = $"{baseId}-{index++}";
        }

        return id;
    }

    private static bool IsGitHubHost(string host)
        => host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
           host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
           host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
           host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase) ||
           host.Equals("github-releases.githubusercontent.com", StringComparison.OrdinalIgnoreCase);
}

public sealed record GitHubProxyEndpoint(
    string Id,
    string Name,
    string UrlTemplate,
    bool Enabled = true,
    bool IsCustom = false)
{
    public string DisplayName => IsCustom ? $"{Name}（自定义）" : Name;
}

public sealed class GitHubProxyDefaults
{
    public string DefaultProxyId { get; set; } = GitHubProxyService.DirectEndpointId;
    public List<GitHubProxyEndpoint> Proxies { get; set; } = [];
}

public sealed class GitHubProxyUserSettings
{
    public string SelectedProxyId { get; set; } = "";
    public List<GitHubProxyEndpoint> CustomProxies { get; set; } = [];
}

public sealed record GitHubDownloadProgress(long BytesReceived, long? TotalBytes);
