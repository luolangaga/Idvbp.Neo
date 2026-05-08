using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Idvbp.Neo.Server.Services;

public interface IFrontendPackageStoreService
{
    FrontendPackageStoreStatus GetStatus();
    Task<IReadOnlyCollection<FrontendPackageStoreItem>> GetPackagesAsync(CancellationToken cancellationToken = default);
    Task<FrontendPackageStoreDetails> GetPackageDetailsAsync(string fileName, CancellationToken cancellationToken = default);
    Task<FrontendPackageInfo> InstallPackageAsync(string fileName, CancellationToken cancellationToken = default);
    Task WritePackageAsync(string fileName, Stream output, CancellationToken cancellationToken = default);
    FrontendPackageStoreAuthState GetAuthState();
    Task SaveTokenAsync(string token, CancellationToken cancellationToken = default);
    Task ClearSavedTokenAsync(CancellationToken cancellationToken = default);
    Task<FrontendPackageStoreDeviceCode> BeginDeviceAuthorizationAsync(CancellationToken cancellationToken = default);
    Task<FrontendPackageStoreDeviceTokenResult> PollDeviceAuthorizationAsync(string deviceCode, CancellationToken cancellationToken = default);
    Task<FrontendPackageStoreUploadResult> UploadFileAsync(IFormFile file, FrontendPackageStoreUploadOptions options, CancellationToken cancellationToken = default);
    Task<FrontendPackageStoreUploadResult> UploadFileAsync(string filePath, FrontendPackageStoreUploadOptions options, CancellationToken cancellationToken = default);
    Task<FrontendPackageStoreUploadResult> UploadLocalPackageAsync(string packageId, FrontendPackageStoreUploadOptions options, CancellationToken cancellationToken = default);
}

public sealed class FrontendPackageStoreService : IFrontendPackageStoreService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly FrontendPackageStoreOptions _options;
    private readonly IGitHubProxyService _gitHubProxyService;
    private readonly IFrontendPackageService _frontendPackageService;
    private readonly HttpClient _httpClient;
    private readonly string _authSettingsPath;

    public FrontendPackageStoreService(
        IConfiguration configuration,
        IGitHubProxyService gitHubProxyService,
        IFrontendPackageService frontendPackageService)
    {
        _options = configuration.GetSection("FrontendPackageStore").Get<FrontendPackageStoreOptions>()
                   ?? new FrontendPackageStoreOptions();
        _options.Token = ResolveToken(configuration, _options.Token);
        _gitHubProxyService = gitHubProxyService;
        _frontendPackageService = frontendPackageService;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Idvbp.Neo");
        _httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _authSettingsPath = Path.Combine(Directory.GetCurrentDirectory(), "data", "frontend-package-store-auth.json");
    }

    public FrontendPackageStoreStatus GetStatus()
        => new(
            IsConfigured,
            _options.Owner,
            _options.Repository,
            string.IsNullOrWhiteSpace(_options.Branch) ? "main" : _options.Branch,
            NormalizeStorePath(_options.Path),
            true,
            !string.IsNullOrWhiteSpace(_options.OAuthClientId),
            GetAuthState().HasSavedToken,
            IsConfigured
                ? $"https://github.com/{_options.Owner}/{_options.Repository}/tree/{Uri.EscapeDataString(Branch)}/{NormalizeStorePath(_options.Path)}"
                : string.Empty);

    public FrontendPackageStoreAuthState GetAuthState()
    {
        var token = LoadSavedToken();
        return new FrontendPackageStoreAuthState(
            !string.IsNullOrWhiteSpace(_options.OAuthClientId),
            !string.IsNullOrWhiteSpace(token));
    }

    public async Task SaveTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Token is required.", nameof(token));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_authSettingsPath))!);
        await using var stream = File.Create(_authSettingsPath);
        await JsonSerializer.SerializeAsync(stream, new FrontendPackageStoreAuthSettings(token.Trim()), JsonOptions, cancellationToken);
    }

    public Task ClearSavedTokenAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_authSettingsPath))
        {
            File.Delete(_authSettingsPath);
        }

        return Task.CompletedTask;
    }

    public async Task<FrontendPackageStoreDeviceCode> BeginDeviceAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OAuthClientId))
        {
            throw new InvalidOperationException("GitHub OAuth ClientId is not configured. Set FrontendPackageStore:OAuthClientId.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, _gitHubProxyService.RewriteUri("https://github.com/login/device/code"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.OAuthClientId,
                ["scope"] = "repo"
            })
        };
        message.Headers.Accept.Clear();
        message.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GitHubDeviceCodeResponse>(JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("GitHub did not return a device authorization code.");
        return new FrontendPackageStoreDeviceCode(
            payload.DeviceCode,
            payload.UserCode,
            payload.VerificationUri,
            payload.ExpiresIn,
            Math.Max(1, payload.Interval));
    }

    public async Task<FrontendPackageStoreDeviceTokenResult> PollDeviceAuthorizationAsync(string deviceCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_options.OAuthClientId))
        {
            throw new InvalidOperationException("GitHub OAuth ClientId is not configured. Set FrontendPackageStore:OAuthClientId.");
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, _gitHubProxyService.RewriteUri("https://github.com/login/oauth/access_token"))
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = _options.OAuthClientId,
                ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            })
        };
        message.Headers.Accept.Clear();
        message.Headers.Accept.ParseAdd("application/json");
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GitHubDeviceTokenResponse>(JsonOptions, cancellationToken)
                      ?? throw new InvalidOperationException("GitHub did not return an OAuth token response.");
        if (!string.IsNullOrWhiteSpace(payload.AccessToken))
        {
            return new FrontendPackageStoreDeviceTokenResult(true, payload.AccessToken, string.Empty);
        }

        return new FrontendPackageStoreDeviceTokenResult(false, string.Empty, payload.Error ?? "authorization_pending");
    }

    public async Task<IReadOnlyCollection<FrontendPackageStoreItem>> GetPackagesAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var items = await GetGitHubJsonAsync<List<GitHubContentItem>>(BuildContentsApiUrl(), cancellationToken) ?? [];

        return items
            .Where(item => string.Equals(item.Type, "file", StringComparison.OrdinalIgnoreCase) &&
                           item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .Select(item => new FrontendPackageStoreItem(
                item.Name,
                Path.GetFileNameWithoutExtension(item.Name),
                string.Empty,
                item.Size,
                item.Sha,
                item.HtmlUrl ?? string.Empty,
                item.DownloadUrl ?? string.Empty,
                item.Path ?? item.Name))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<FrontendPackageInfo> InstallPackageAsync(string fileName, CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await WritePackageAsync(fileName, stream, cancellationToken);
        stream.Position = 0;
        return await _frontendPackageService.ImportAsync(new FormFile(stream, 0, stream.Length, "file", fileName), cancellationToken);
    }

    public async Task<FrontendPackageStoreDetails> GetPackageDetailsAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var safeName = SanitizeZipFileName(fileName);
        var item = await GetContentItemAsync(safeName, cancellationToken)
                   ?? throw new KeyNotFoundException($"Store package '{safeName}' was not found.");
        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            throw new InvalidOperationException("GitHub did not provide a package download URL.");
        }

        var bytes = await _gitHubProxyService.GetByteArrayAsync(item.DownloadUrl, cancellationToken);
        await using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Package must contain manifest.json.");

        await using var manifestStream = manifestEntry.Open();
        var manifest = await JsonSerializer.DeserializeAsync<FrontendManifest>(manifestStream, JsonOptions, cancellationToken)
                       ?? throw new InvalidOperationException("manifest.json is invalid.");

        var pages = manifest.Pages
            .Where(page => !string.IsNullOrWhiteSpace(page.Id))
            .Select(page => new FrontendPackageStorePageDetails(
                page.Id,
                string.IsNullOrWhiteSpace(page.Name) ? page.Id : page.Name,
                page.Layout ?? string.Empty))
            .ToArray();

        var componentCount = archive.Entries.Count(entry =>
            entry.FullName.Replace('\\', '/').Contains("/components/", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(entry.Name));
        var assetCount = archive.Entries.Count(entry =>
            entry.FullName.Replace('\\', '/').Contains("/assets/", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrEmpty(entry.Name));

        return new FrontendPackageStoreDetails(
            safeName,
            string.IsNullOrWhiteSpace(manifest.Id) ? Path.GetFileNameWithoutExtension(safeName) : manifest.Id,
            string.IsNullOrWhiteSpace(manifest.Name) ? Path.GetFileNameWithoutExtension(safeName) : manifest.Name,
            manifest.Version ?? string.Empty,
            manifest.Type ?? "layout-template",
            manifest.EntryLayout ?? "layout.json",
            item.Size,
            item.HtmlUrl ?? string.Empty,
            pages,
            componentCount,
            assetCount);
    }

    public async Task WritePackageAsync(string fileName, Stream output, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var safeName = SanitizeZipFileName(fileName);
        var item = await GetContentItemAsync(safeName, cancellationToken)
                   ?? throw new KeyNotFoundException($"Store package '{safeName}' was not found.");

        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            throw new InvalidOperationException("GitHub did not provide a package download URL.");
        }

        var bytes = await _gitHubProxyService.GetByteArrayAsync(item.DownloadUrl, cancellationToken);
        await output.WriteAsync(bytes, cancellationToken);
    }

    public async Task<FrontendPackageStoreUploadResult> UploadFileAsync(IFormFile file, FrontendPackageStoreUploadOptions options, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var token = ResolveUploadToken(options);

        if (file.Length == 0)
        {
            throw new ArgumentException("Package file is empty.", nameof(file));
        }

        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var manifest = ReadManifest(buffer);
        var fileName = CreatePackageFileName(manifest, file.FileName);
        return await UploadBytesAsync(fileName, buffer.ToArray(), manifest, token, options, cancellationToken);
    }

    public async Task<FrontendPackageStoreUploadResult> UploadFileAsync(string filePath, FrontendPackageStoreUploadOptions options, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var token = ResolveUploadToken(options);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Package file was not found.", filePath);
        }

        await using var input = File.OpenRead(filePath);
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer, cancellationToken);
        var manifest = ReadManifest(buffer);
        var fileName = CreatePackageFileName(manifest, Path.GetFileName(filePath));
        return await UploadBytesAsync(fileName, buffer.ToArray(), manifest, token, options, cancellationToken);
    }

    public async Task<FrontendPackageStoreUploadResult> UploadLocalPackageAsync(string packageId, FrontendPackageStoreUploadOptions options, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var token = ResolveUploadToken(options);

        var package = _frontendPackageService.GetPackage(packageId)
                      ?? throw new KeyNotFoundException($"Frontend package '{packageId}' was not found.");
        await using var buffer = new MemoryStream();
        await _frontendPackageService.WritePackageZipAsync(package.Id, buffer, cancellationToken);
        var manifest = new FrontendStoreManifest(package.Id, package.Name, package.Version);
        var fileName = CreatePackageFileName(manifest, $"{package.Id}.zip");
        return await UploadBytesAsync(fileName, buffer.ToArray(), manifest, token, options, cancellationToken);
    }

    private async Task<FrontendPackageStoreUploadResult> UploadBytesAsync(
        string fileName,
        byte[] bytes,
        FrontendStoreManifest manifest,
        string token,
        FrontendPackageStoreUploadOptions options,
        CancellationToken cancellationToken)
    {
        var direct = await TryPutContentAsync(fileName, bytes, manifest, Branch, token, cancellationToken);
        if (direct.Success)
        {
            return direct.Result!;
        }

        if (!options.CreatePullRequestOnFailure)
        {
            throw new InvalidOperationException(direct.ErrorMessage);
        }

        return await UploadByPullRequestAsync(fileName, bytes, manifest, token, direct.ErrorMessage, cancellationToken);
    }

    private async Task<FrontendPackageStoreUploadResult> UploadByPullRequestAsync(
        string fileName,
        byte[] bytes,
        FrontendStoreManifest manifest,
        string token,
        string directError,
        CancellationToken cancellationToken)
    {
        var baseSha = await GetBranchHeadShaAsync(Branch, token, cancellationToken);
        var branchName = $"frontend-package/{manifest.Id}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        await CreateBranchAsync(branchName, baseSha, token, cancellationToken);

        var upload = await TryPutContentAsync(fileName, bytes, manifest, branchName, token, cancellationToken);
        if (!upload.Success)
        {
            throw new InvalidOperationException($"直推失败后尝试创建 PR，但上传到 PR 分支仍失败。直推错误：{directError}。PR 分支错误：{upload.ErrorMessage}");
        }

        var prUrl = await CreatePullRequestAsync(fileName, branchName, token, cancellationToken);
        return upload.Result! with
        {
            SubmittedPullRequest = true,
            PullRequestUrl = prUrl
        };
    }

    private async Task<UploadAttemptResult> TryPutContentAsync(
        string fileName,
        byte[] bytes,
        FrontendStoreManifest manifest,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        var existing = await GetContentItemAsync(fileName, branch, token, cancellationToken);
        var request = new GitHubPutContentRequest(
            $"Upload frontend package {fileName}",
            Convert.ToBase64String(bytes),
            branch,
            existing?.Sha);

        using var message = new HttpRequestMessage(HttpMethod.Put, _gitHubProxyService.RewriteUri(BuildContentApiUrl(fileName)))
        {
            Content = JsonContent.Create(request, options: JsonOptions)
        };
        ApplyGitHubHeaders(message, token);

        using var response = await _httpClient.SendAsync(message, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return UploadAttemptResult.Failed($"GitHub 上传失败：{(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        return UploadAttemptResult.Ok(new FrontendPackageStoreUploadResult(
            fileName,
            manifest.Id,
            manifest.Name,
            manifest.Version,
            bytes.LongLength,
            existing is not null,
            false,
            string.Empty));
    }

    private async Task<GitHubContentItem?> GetContentItemAsync(string fileName, CancellationToken cancellationToken)
        => await GetContentItemAsync(fileName, Branch, string.Empty, cancellationToken);

    private async Task<GitHubContentItem?> GetContentItemAsync(string fileName, string branch, string token, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token) && string.Equals(branch, Branch, StringComparison.OrdinalIgnoreCase))
            {
                return await GetGitHubJsonAsync<GitHubContentItem>(BuildContentApiUrl(fileName), cancellationToken);
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, _gitHubProxyService.RewriteUri($"{BuildContentApiUrl(fileName)}?ref={Uri.EscapeDataString(branch)}"));
            ApplyGitHubHeaders(message, token);
            using var response = await _httpClient.SendAsync(message, cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<GitHubContentItem>(JsonOptions, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<T?> GetGitHubJsonAsync<T>(string url, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(_gitHubProxyService.RewriteUri(url), cancellationToken);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        text = TrimJsonPrefix(StripAnsi(text));
        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOptions);
        }
        catch (JsonException exception)
        {
            var preview = text.Length > 180 ? text[..180] + "..." : text;
            throw new InvalidOperationException($"GitHub 返回的内容不是有效 JSON，请检查商店仓库配置或 GitHub 代理。响应开头：{preview}", exception);
        }
    }

    private static string StripAnsi(string value)
        => Regex.Replace(value, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);

    private static string TrimJsonPrefix(string value)
    {
        var index = value.IndexOfAny(['{', '[']);
        if (index > 0)
        {
            value = value[index..];
        }

        return value.TrimStart('\uFEFF', '\u200B').TrimStart();
    }

    private async Task<string> GetBranchHeadShaAsync(string branch, string token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, _gitHubProxyService.RewriteUri(
            $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/git/ref/heads/{Uri.EscapeDataString(branch)}"));
        ApplyGitHubHeaders(message, token);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GitHubRefResponse>(JsonOptions, cancellationToken);
        return payload?.Object?.Sha ?? throw new InvalidOperationException("GitHub did not return base branch SHA.");
    }

    private async Task CreateBranchAsync(string branch, string sha, string token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _gitHubProxyService.RewriteUri(
            $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/git/refs"))
        {
            Content = JsonContent.Create(new GitHubCreateRefRequest($"refs/heads/{branch}", sha), options: JsonOptions)
        };
        ApplyGitHubHeaders(message, token);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> CreatePullRequestAsync(string fileName, string branch, string token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, _gitHubProxyService.RewriteUri(
            $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/pulls"))
        {
            Content = JsonContent.Create(new GitHubCreatePullRequest(
                $"上传页面包 {fileName}",
                branch,
                Branch,
                $"由 Idvbp.Neo 页面包商店自动提交。\n\n文件：`{fileName}`\n\n请审核后合并，合并后会自动出现在商店列表中。"),
                options: JsonOptions)
        };
        ApplyGitHubHeaders(message, token);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GitHubPullRequestResponse>(JsonOptions, cancellationToken);
        return payload?.HtmlUrl ?? string.Empty;
    }

    private FrontendStoreManifest ReadManifest(Stream packageStream)
    {
        packageStream.Position = 0;
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, leaveOpen: true);
        var manifestEntry = archive.Entries.FirstOrDefault(entry =>
            string.Equals(Path.GetFileName(entry.FullName), "manifest.json", StringComparison.OrdinalIgnoreCase));
        if (manifestEntry is null)
        {
            throw new InvalidOperationException("Package must contain manifest.json.");
        }

        using var manifestStream = manifestEntry.Open();
        var manifest = JsonSerializer.Deserialize<FrontendManifest>(manifestStream, JsonOptions)
                       ?? throw new InvalidOperationException("manifest.json is invalid.");
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            throw new InvalidOperationException("manifest.json id is required.");
        }

        packageStream.Position = 0;
        return new FrontendStoreManifest(
            SanitizeNamePart(manifest.Id),
            string.IsNullOrWhiteSpace(manifest.Name) ? manifest.Id : manifest.Name,
            manifest.Version ?? string.Empty);
    }

    private string BuildContentsApiUrl()
        => $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/contents/{EscapePath(StorePath)}?ref={Uri.EscapeDataString(Branch)}";

    private string BuildContentApiUrl(string fileName)
    {
        var path = string.IsNullOrWhiteSpace(StorePath)
            ? Uri.EscapeDataString(fileName)
            : $"{EscapePath(StorePath)}/{Uri.EscapeDataString(fileName)}";
        return $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/contents/{path}";
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Frontend package store is not configured. Set FrontendPackageStore:Owner and FrontendPackageStore:Repository.");
        }
    }

    private string ResolveUploadToken(FrontendPackageStoreUploadOptions options)
    {
        var token = FirstNonEmpty(options.Token, LoadSavedToken(), _options.Token);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("上传需要 GitHub Token。请在提交窗口粘贴 Token，本应用不会保存它。");
        }

        return token;
    }

    private static void ApplyGitHubHeaders(HttpRequestMessage message, string token)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        message.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
    }

    private bool IsConfigured
        => !string.IsNullOrWhiteSpace(_options.Owner) &&
           !string.IsNullOrWhiteSpace(_options.Repository);

    private string Branch => string.IsNullOrWhiteSpace(_options.Branch) ? "main" : _options.Branch.Trim();

    private string StorePath => NormalizeStorePath(_options.Path);

    private static string NormalizeStorePath(string value)
        => value.Replace('\\', '/').Trim('/');

    private static string EscapePath(string value)
        => string.Join(
            '/',
            NormalizeStorePath(value)
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(Uri.EscapeDataString));

    private static string ResolveToken(IConfiguration configuration, string configuredToken)
        => FirstNonEmpty(
            configuredToken,
            configuration["IDVBP_NEO_GITHUB_TOKEN"],
            configuration["GITHUB_TOKEN"],
            Environment.GetEnvironmentVariable("IDVBP_NEO_GITHUB_TOKEN"),
            Environment.GetEnvironmentVariable("GITHUB_TOKEN"));

    private string LoadSavedToken()
    {
        if (!File.Exists(_authSettingsPath))
        {
            return string.Empty;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<FrontendPackageStoreAuthSettings>(
                File.ReadAllText(_authSettingsPath),
                JsonOptions);
            return settings?.Token ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string CreatePackageFileName(FrontendStoreManifest manifest, string fallbackFileName)
    {
        var id = SanitizeNamePart(manifest.Id);
        if (string.IsNullOrWhiteSpace(id))
        {
            id = SanitizeNamePart(Path.GetFileNameWithoutExtension(fallbackFileName));
        }

        var version = SanitizeNamePart(manifest.Version);
        return string.IsNullOrWhiteSpace(version) ? $"{id}.zip" : $"{id}-{version}.zip";
    }

    private static string SanitizeZipFileName(string fileName)
    {
        var safe = Path.GetFileName(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safe) || !safe.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A zip package file name is required.", nameof(fileName));
        }

        return safe;
    }

    private static string SanitizeNamePart(string? value)
    {
        var result = string.Concat((value ?? string.Empty).Trim()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-'))
            .Trim('-', '.', '_');
        return string.IsNullOrWhiteSpace(result) ? "package" : result;
    }

    private sealed record FrontendStoreManifest(string Id, string Name, string Version);

    private sealed class GitHubContentItem
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Sha { get; set; } = "";
        public long Size { get; set; }
        public string Type { get; set; } = "";
        [JsonPropertyName("download_url")]
        public string? DownloadUrl { get; set; }
        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }

    private sealed record GitHubPutContentRequest(
        string Message,
        string Content,
        string Branch,
        string? Sha);

    private sealed record GitHubCreateRefRequest(string Ref, string Sha);

    private sealed record GitHubCreatePullRequest(string Title, string Head, string Base, string Body);

    private sealed class GitHubRefResponse
    {
        public GitHubObjectResponse? Object { get; set; }
    }

    private sealed class GitHubObjectResponse
    {
        public string Sha { get; set; } = "";
    }

    private sealed class GitHubPullRequestResponse
    {
        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";
    }

    private sealed class GitHubDeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = "";

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = "";

        [JsonPropertyName("verification_uri")]
        public string VerificationUri { get; set; } = "";

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; } = 5;
    }

    private sealed class GitHubDeviceTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = "";

        public string? Error { get; set; }
    }

    private sealed record UploadAttemptResult(
        bool Success,
        FrontendPackageStoreUploadResult? Result,
        string ErrorMessage)
    {
        public static UploadAttemptResult Ok(FrontendPackageStoreUploadResult result)
            => new(true, result, string.Empty);

        public static UploadAttemptResult Failed(string message)
            => new(false, null, message);
    }
}

public sealed class FrontendPackageStoreOptions
{
    public string Owner { get; set; } = "";
    public string Repository { get; set; } = "";
    public string Branch { get; set; } = "main";
    public string Path { get; set; } = "packages";
    public string Token { get; set; } = "";
    public string OAuthClientId { get; set; } = "";
}

public sealed record FrontendPackageStoreStatus(
    bool Configured,
    string Owner,
    string Repository,
    string Branch,
    string Path,
    bool CanUpload,
    bool OAuthConfigured,
    bool HasSavedToken,
    string RepositoryUrl);

public sealed record FrontendPackageStoreAuthState(bool OAuthConfigured, bool HasSavedToken);

public sealed record FrontendPackageStoreAuthSettings(string Token);

public sealed record FrontendPackageStoreDeviceCode(
    string DeviceCode,
    string UserCode,
    string VerificationUri,
    int ExpiresIn,
    int Interval);

public sealed record FrontendPackageStoreDeviceTokenResult(
    bool Success,
    string AccessToken,
    string Error);

public sealed record FrontendPackageStoreItem(
    string FileName,
    string Name,
    string Version,
    long SizeBytes,
    string Sha,
    string HtmlUrl,
    string DownloadUrl,
    string Path);

public sealed record FrontendPackageStoreUploadResult(
    string FileName,
    string PackageId,
    string Name,
    string Version,
    long SizeBytes,
    bool Updated,
    bool SubmittedPullRequest,
    string PullRequestUrl);

public sealed record FrontendPackageStoreUploadOptions(
    string? Token,
    bool CreatePullRequestOnFailure = true);

public sealed record FrontendPackageStoreDetails(
    string FileName,
    string PackageId,
    string Name,
    string Version,
    string Type,
    string EntryLayout,
    long SizeBytes,
    string HtmlUrl,
    IReadOnlyCollection<FrontendPackageStorePageDetails> Pages,
    int ComponentFileCount,
    int AssetFileCount);

public sealed record FrontendPackageStorePageDetails(string Id, string Name, string Layout);
