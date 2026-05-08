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
    Task<FrontendPackageStoreDetails> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default);
    Task<FrontendPackageInfo> InstallPackageAsync(string packageId, IProgress<FrontendPackageTransferProgress>? progress = null, CancellationToken cancellationToken = default);
    Task WritePackageAsync(string packageId, Stream output, IProgress<FrontendPackageTransferProgress>? progress = null, CancellationToken cancellationToken = default);
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
    private const string PackageZipFileName = "package.zip";
    private const string StoreMetadataFileName = "store.json";

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

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/device/code")
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

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://github.com/login/oauth/access_token")
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
        var result = new List<FrontendPackageStoreItem>();

        foreach (var directory in items.Where(item => string.Equals(item.Type, "dir", StringComparison.OrdinalIgnoreCase)))
        {
            var packageId = SanitizeNamePart(directory.Name);
            var zip = await GetContentItemByPathAsync(BuildStoreRelativePath(packageId, PackageZipFileName), Branch, string.Empty, cancellationToken);
            if (zip is null)
            {
                continue;
            }

            var metadata = await ReadStoreMetadataAsync(packageId, cancellationToken) ??
                           new FrontendPackageStoreMetadata(packageId, string.Empty, packageId, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            result.Add(new FrontendPackageStoreItem(
                packageId,
                $"{packageId}/{PackageZipFileName}",
                metadata.Name,
                string.Empty,
                metadata.Description,
                metadata.AuthorName,
                metadata.ScreenshotUrl,
                metadata.Website,
                metadata.Contact,
                zip.Size,
                zip.Sha,
                zip.HtmlUrl ?? string.Empty,
                zip.DownloadUrl ?? string.Empty,
                zip.Path ?? BuildStoreRelativePath(packageId, PackageZipFileName)));
        }

        foreach (var zip in items.Where(item => string.Equals(item.Type, "file", StringComparison.OrdinalIgnoreCase) &&
                                                item.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
        {
            var packageId = SanitizeNamePart(Path.GetFileNameWithoutExtension(zip.Name));
            result.Add(new FrontendPackageStoreItem(
                packageId,
                zip.Name,
                packageId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                zip.Size,
                zip.Sha,
                zip.HtmlUrl ?? string.Empty,
                zip.DownloadUrl ?? string.Empty,
                zip.Path ?? zip.Name));
        }

        return result
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<FrontendPackageInfo> InstallPackageAsync(
        string packageId,
        IProgress<FrontendPackageTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new MemoryStream();
        await WritePackageAsync(packageId, stream, progress, cancellationToken);
        progress?.Report(new FrontendPackageTransferProgress(stream.Length, stream.Length, "正在导入页面包"));
        stream.Position = 0;
        return await _frontendPackageService.ImportAsync(new FormFile(stream, 0, stream.Length, "file", $"{packageId}.zip"), cancellationToken);
    }

    public async Task<FrontendPackageStoreDetails> GetPackageDetailsAsync(string packageId, CancellationToken cancellationToken = default)
    {
        var safePackageId = SanitizeNamePart(packageId);
        var item = await GetPackageZipContentItemAsync(safePackageId, cancellationToken)
                   ?? throw new KeyNotFoundException($"Store package '{safePackageId}' was not found.");
        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            throw new InvalidOperationException("GitHub did not provide a package download URL.");
        }

        var metadata = await ReadStoreMetadataAsync(safePackageId, cancellationToken);
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
            safePackageId,
            $"{safePackageId}/{PackageZipFileName}",
            string.IsNullOrWhiteSpace(manifest.Id) ? safePackageId : manifest.Id,
            metadata?.Name ?? (string.IsNullOrWhiteSpace(manifest.Name) ? safePackageId : manifest.Name),
            manifest.Version ?? string.Empty,
            manifest.Type ?? "layout-template",
            manifest.EntryLayout ?? "layout.json",
            metadata?.PageId ?? string.Empty,
            metadata?.Description ?? string.Empty,
            metadata?.AuthorName ?? string.Empty,
            metadata?.ScreenshotUrl ?? string.Empty,
            metadata?.Website ?? string.Empty,
            metadata?.Contact ?? string.Empty,
            item.Size,
            item.HtmlUrl ?? string.Empty,
            pages,
            componentCount,
            assetCount);
    }

    public async Task WritePackageAsync(
        string packageId,
        Stream output,
        IProgress<FrontendPackageTransferProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var safePackageId = SanitizeNamePart(packageId);
        var item = await GetPackageZipContentItemAsync(safePackageId, cancellationToken)
                   ?? throw new KeyNotFoundException($"Store package '{safePackageId}' was not found.");

        if (string.IsNullOrWhiteSpace(item.DownloadUrl))
        {
            throw new InvalidOperationException("GitHub did not provide a package download URL.");
        }

        await _gitHubProxyService.DownloadAsync(
            item.DownloadUrl,
            output,
            progress is null
                ? null
                : new Progress<GitHubDownloadProgress>(value =>
                    progress.Report(new FrontendPackageTransferProgress(value.BytesReceived, value.TotalBytes, "正在下载页面包"))),
            cancellationToken);
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
        var metadata = NormalizeUploadMetadata(options.Metadata, manifest);
        return await UploadBytesAsync(metadata.PackageId, buffer.ToArray(), manifest, metadata, token, options, cancellationToken);
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
        var metadata = NormalizeUploadMetadata(options.Metadata, manifest);
        return await UploadBytesAsync(metadata.PackageId, buffer.ToArray(), manifest, metadata, token, options, cancellationToken);
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
        var metadata = NormalizeUploadMetadata(options.Metadata, manifest);
        return await UploadBytesAsync(metadata.PackageId, buffer.ToArray(), manifest, metadata, token, options, cancellationToken);
    }

    private async Task<FrontendPackageStoreUploadResult> UploadBytesAsync(
        string packageId,
        byte[] bytes,
        FrontendStoreManifest manifest,
        FrontendPackageStoreMetadata metadata,
        string token,
        FrontendPackageStoreUploadOptions options,
        CancellationToken cancellationToken)
    {
        var direct = await TryPutStorePackageAsync(packageId, bytes, manifest, metadata, Branch, token, cancellationToken);
        if (direct.Success)
        {
            return direct.Result!;
        }

        if (!options.CreatePullRequestOnFailure)
        {
            throw new InvalidOperationException(direct.ErrorMessage);
        }

        return await UploadByPullRequestAsync(packageId, bytes, manifest, metadata, token, direct.ErrorMessage, cancellationToken);
    }

    private async Task<FrontendPackageStoreUploadResult> UploadByPullRequestAsync(
        string packageId,
        byte[] bytes,
        FrontendStoreManifest manifest,
        FrontendPackageStoreMetadata metadata,
        string token,
        string directError,
        CancellationToken cancellationToken)
    {
        var baseSha = await GetBranchHeadShaAsync(Branch, token, cancellationToken);
        var branchName = $"frontend-package/{packageId}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        await CreateBranchAsync(branchName, baseSha, token, cancellationToken);

        var upload = await TryPutStorePackageAsync(packageId, bytes, manifest, metadata, branchName, token, cancellationToken);
        if (!upload.Success)
        {
            throw new InvalidOperationException($"直推失败后尝试创建 PR，但上传到 PR 分支仍失败。直推错误：{directError}。PR 分支错误：{upload.ErrorMessage}");
        }

        var prUrl = await CreatePullRequestAsync(packageId, branchName, token, cancellationToken);
        return upload.Result! with
        {
            SubmittedPullRequest = true,
            PullRequestUrl = prUrl
        };
    }

    private async Task<UploadAttemptResult> TryPutStorePackageAsync(
        string packageId,
        byte[] bytes,
        FrontendStoreManifest manifest,
        FrontendPackageStoreMetadata metadata,
        string branch,
        string token,
        CancellationToken cancellationToken)
    {
        var zipPath = BuildStoreRelativePath(packageId, PackageZipFileName);
        var metadataPath = BuildStoreRelativePath(packageId, StoreMetadataFileName);
        var existingZip = await GetContentItemByPathAsync(zipPath, branch, token, cancellationToken);
        var zipResult = await TryPutContentPathAsync(zipPath, bytes, $"Upload frontend package {packageId}", branch, existingZip?.Sha, token, cancellationToken);
        if (!zipResult.Success)
        {
            return zipResult;
        }

        var metadataBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(metadata, new JsonSerializerOptions(JsonOptions)
        {
            WriteIndented = true
        }));
        var existingMetadata = await GetContentItemByPathAsync(metadataPath, branch, token, cancellationToken);
        var metadataResult = await TryPutContentPathAsync(metadataPath, metadataBytes, $"Update frontend package metadata {packageId}", branch, existingMetadata?.Sha, token, cancellationToken);
        if (!metadataResult.Success)
        {
            return metadataResult;
        }

        return UploadAttemptResult.Ok(new FrontendPackageStoreUploadResult(
            $"{packageId}/{PackageZipFileName}",
            manifest.Id,
            metadata.Name,
            manifest.Version,
            bytes.LongLength,
            existingZip is not null,
            false,
            string.Empty));
    }

    private async Task<UploadAttemptResult> TryPutContentPathAsync(
        string relativePath,
        byte[] bytes,
        string messageText,
        string branch,
        string? sha,
        string token,
        CancellationToken cancellationToken)
    {
        var request = new GitHubPutContentRequest(
            messageText,
            Convert.ToBase64String(bytes),
            branch,
            sha);

        using var message = new HttpRequestMessage(HttpMethod.Put, BuildContentApiUrl(relativePath))
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

        return UploadAttemptResult.Ok(null!);
    }

    private async Task<GitHubContentItem?> GetContentItemAsync(string fileName, CancellationToken cancellationToken)
        => await GetContentItemByPathAsync(fileName, Branch, string.Empty, cancellationToken);

    private async Task<GitHubContentItem?> GetPackageZipContentItemAsync(string packageId, CancellationToken cancellationToken)
    {
        var directoryZip = await GetContentItemByPathAsync(BuildStoreRelativePath(packageId, PackageZipFileName), Branch, string.Empty, cancellationToken);
        if (directoryZip is not null)
        {
            return directoryZip;
        }

        return await GetContentItemByPathAsync($"{packageId}.zip", Branch, string.Empty, cancellationToken);
    }

    private async Task<GitHubContentItem?> GetContentItemByPathAsync(string relativePath, string branch, string token, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(token) && string.Equals(branch, Branch, StringComparison.OrdinalIgnoreCase))
            {
                return await GetGitHubJsonAsync<GitHubContentItem>(BuildContentApiUrl(relativePath), cancellationToken);
            }

            using var message = new HttpRequestMessage(HttpMethod.Get, $"{BuildContentApiUrl(relativePath)}?ref={Uri.EscapeDataString(branch)}");
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
        using var response = await _httpClient.GetAsync(url, cancellationToken);
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
        using var message = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/git/ref/heads/{Uri.EscapeDataString(branch)}");
        ApplyGitHubHeaders(message, token);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<GitHubRefResponse>(JsonOptions, cancellationToken);
        return payload?.Object?.Sha ?? throw new InvalidOperationException("GitHub did not return base branch SHA.");
    }

    private async Task CreateBranchAsync(string branch, string sha, string token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/git/refs")
        {
            Content = JsonContent.Create(new GitHubCreateRefRequest($"refs/heads/{branch}", sha), options: JsonOptions)
        };
        ApplyGitHubHeaders(message, token);
        using var response = await _httpClient.SendAsync(message, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string> CreatePullRequestAsync(string packageId, string branch, string token, CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/pulls")
        {
            Content = JsonContent.Create(new GitHubCreatePullRequest(
                $"上传页面包 {packageId}",
                branch,
                Branch,
                $"由 Idvbp.Neo 页面包商店自动提交。\n\n页面包：`{packageId}`\n\n请审核后合并，合并后会自动出现在商店列表中。"),
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

    private async Task<FrontendPackageStoreMetadata?> ReadStoreMetadataAsync(string packageId, CancellationToken cancellationToken)
    {
        var item = await GetContentItemByPathAsync(BuildStoreRelativePath(packageId, StoreMetadataFileName), Branch, string.Empty, cancellationToken);
        if (item?.DownloadUrl is null)
        {
            return null;
        }

        var text = await _httpClient.GetStringAsync(item.DownloadUrl, cancellationToken);
        return JsonSerializer.Deserialize<FrontendPackageStoreMetadata>(text, JsonOptions);
    }

    private static FrontendPackageStoreMetadata NormalizeUploadMetadata(
        FrontendPackageStoreMetadata? metadata,
        FrontendStoreManifest manifest)
    {
        var packageId = SanitizeNamePart(metadata?.PackageId ?? manifest.Id);
        return new FrontendPackageStoreMetadata(
            packageId,
            metadata?.PageId?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(metadata?.Name) ? manifest.Name : metadata.Name.Trim(),
            metadata?.Description?.Trim() ?? string.Empty,
            metadata?.AuthorName?.Trim() ?? string.Empty,
            metadata?.ScreenshotUrl?.Trim() ?? string.Empty,
            metadata?.Website?.Trim() ?? string.Empty,
            metadata?.Contact?.Trim() ?? string.Empty);
    }

    private string BuildContentsApiUrl()
        => $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/contents/{EscapePath(StorePath)}?ref={Uri.EscapeDataString(Branch)}";

    private string BuildContentApiUrl(string fileName)
    {
        var path = string.IsNullOrWhiteSpace(StorePath)
            ? EscapePath(fileName)
            : $"{EscapePath(StorePath)}/{EscapePath(fileName)}";
        return $"https://api.github.com/repos/{_options.Owner}/{_options.Repository}/contents/{path}";
    }

    private static string BuildStoreRelativePath(string packageId, string fileName)
        => $"{SanitizeNamePart(packageId)}/{fileName}";

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

public sealed record FrontendPackageTransferProgress(
    long BytesReceived,
    long? TotalBytes,
    string Stage);

public sealed record FrontendPackageStoreItem(
    string PackageId,
    string FileName,
    string Name,
    string Version,
    string Description,
    string AuthorName,
    string ScreenshotUrl,
    string Website,
    string Contact,
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
    bool CreatePullRequestOnFailure = true,
    FrontendPackageStoreMetadata? Metadata = null);

public sealed record FrontendPackageStoreMetadata(
    string PackageId,
    string PageId,
    string Name,
    string Description,
    string AuthorName,
    string ScreenshotUrl,
    string Website,
    string Contact);

public sealed record FrontendPackageStoreDetails(
    string StorePackageId,
    string FileName,
    string PackageId,
    string Name,
    string Version,
    string Type,
    string EntryLayout,
    string PageId,
    string Description,
    string AuthorName,
    string ScreenshotUrl,
    string Website,
    string Contact,
    long SizeBytes,
    string HtmlUrl,
    IReadOnlyCollection<FrontendPackageStorePageDetails> Pages,
    int ComponentFileCount,
    int AssetFileCount);

public sealed record FrontendPackageStorePageDetails(string Id, string Name, string Layout);
