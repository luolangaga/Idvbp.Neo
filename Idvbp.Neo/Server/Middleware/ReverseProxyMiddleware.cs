using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;

namespace Idvbp.Neo.Server.Middleware;

public sealed class ReverseProxyConfig
{
    public bool Enabled { get; set; } = true;
    public List<ReverseProxyRoute> Routes { get; set; } = [];
}

public sealed class ReverseProxyRoute
{
    public string Id { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = "";
    public string PathPrefix { get; set; } = "/proxy";
    public string TargetUrl { get; set; } = "";
    public string StaticRoot { get; set; } = "";
    public bool EnableSpaFallback { get; set; } = true;
    public string SpaFallbackFile { get; set; } = "index.html";
}

public sealed class ReverseProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ReverseProxyConfig _config;
    private readonly string _wwwrootPath;
    private readonly HttpClient _httpClient;

    public ReverseProxyMiddleware(RequestDelegate next, ReverseProxyConfig config, string wwwrootPath)
    {
        _next = next;
        _config = config;
        _wwwrootPath = wwwrootPath;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!_config.Enabled)
        {
            await _next(context);
            return;
        }

        var route = _config.Routes
            .Where(x => x.Enabled)
            .OrderByDescending(x => NormalizePrefix(x.PathPrefix).Length)
            .FirstOrDefault(x => context.Request.Path.StartsWithSegments(NormalizePrefix(x.PathPrefix), StringComparison.OrdinalIgnoreCase));

        if (route == null)
        {
            await _next(context);
            return;
        }

        var path = GetPathAfterPrefix(context.Request.Path, route.PathPrefix);
        if (!string.IsNullOrWhiteSpace(route.StaticRoot))
        {
            await ServeStaticRouteAsync(context, route, path);
            return;
        }

        await ProxyToTargetAsync(context, route, path);
    }

    private async Task ProxyToTargetAsync(HttpContext context, ReverseProxyRoute route, string path)
    {
        if (string.IsNullOrWhiteSpace(route.TargetUrl))
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("Proxy target is not configured.");
            return;
        }

        var targetUrl = $"{route.TargetUrl.TrimEnd('/')}{path}{context.Request.QueryString}";

        try
        {
            using var request = new HttpRequestMessage
            {
                Method = new HttpMethod(context.Request.Method),
                RequestUri = new Uri(targetUrl)
            };

            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                    header.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            if (context.Request.ContentLength > 0)
            {
                request.Content = new StreamContent(context.Request.Body);
                if (!string.IsNullOrWhiteSpace(context.Request.ContentType))
                {
                    request.Content.Headers.TryAddWithoutValidation("Content-Type", context.Request.ContentType);
                }
            }

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            if (response.StatusCode == HttpStatusCode.NotFound && route.EnableSpaFallback && IsSpaFallbackRequest(context.Request, path))
            {
                await ProxySpaFallbackAsync(context, route);
                return;
            }

            await CopyProxyResponseAsync(context, response);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
            await context.Response.WriteAsync("502 Bad Gateway - Proxy target unreachable");
        }
        catch (TaskCanceledException)
        {
            context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            await context.Response.WriteAsync("504 Gateway Timeout");
        }
    }

    private async Task ProxySpaFallbackAsync(HttpContext context, ReverseProxyRoute route)
    {
        var fallbackUrl = $"{route.TargetUrl.TrimEnd('/')}/{route.SpaFallbackFile.TrimStart('/')}";
        using var fallbackRequest = new HttpRequestMessage(HttpMethod.Get, fallbackUrl);
        using var fallbackResponse = await _httpClient.SendAsync(
            fallbackRequest,
            HttpCompletionOption.ResponseHeadersRead,
            context.RequestAborted);

        await CopyProxyResponseAsync(context, fallbackResponse);
    }

    private async Task ServeStaticRouteAsync(HttpContext context, ReverseProxyRoute route, string path)
    {
        var root = Path.Combine(_wwwrootPath, route.StaticRoot.Trim('/', '\\'));
        var requestedPath = path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.GetFullPath(Path.Combine(root, requestedPath));
        var rootPath = Path.GetFullPath(root);

        if (!filePath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (Directory.Exists(filePath))
        {
            filePath = Path.Combine(filePath, "index.html");
        }

        if (!File.Exists(filePath) && route.EnableSpaFallback && IsSpaFallbackRequest(context.Request, path))
        {
            filePath = Path.Combine(rootPath, route.SpaFallbackFile);
        }

        if (!File.Exists(filePath))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var provider = new FileExtensionContentTypeProvider();
        if (provider.TryGetContentType(filePath, out var contentType))
        {
            context.Response.ContentType = contentType;
        }

        context.Response.Headers.CacheControl = "no-store, no-cache, max-age=0";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
        context.Response.StatusCode = StatusCodes.Status200OK;
        await context.Response.SendFileAsync(filePath, context.RequestAborted);
    }

    private static async Task CopyProxyResponseAsync(HttpContext context, HttpResponseMessage response)
    {
        context.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            if (!header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in response.Content.Headers)
        {
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        context.Response.Headers.Remove("transfer-encoding");
        await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
    }

    private static bool IsSpaFallbackRequest(HttpRequest request, string path)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (Path.HasExtension(path))
        {
            return false;
        }

        return request.Headers.Accept.Count == 0 ||
               request.Headers.Accept.Any(x => x?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static string GetPathAfterPrefix(PathString requestPath, string prefix)
    {
        var normalizedPrefix = NormalizePrefix(prefix);
        var value = requestPath.Value ?? "/";
        var path = value.Length >= normalizedPrefix.Length ? value[normalizedPrefix.Length..] : "/";
        return string.IsNullOrEmpty(path) ? "/" : path.StartsWith('/') ? path : "/" + path;
    }

    private static string NormalizePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return "/";
        }

        return prefix.StartsWith('/') ? prefix.TrimEnd('/') : "/" + prefix.TrimEnd('/');
    }
}

public static class ReverseProxyConfigLoader
{
    private static readonly object SyncRoot = new();

    public static string ResolveConfigPath()
    {
        var outputPath = Path.Combine(AppContext.BaseDirectory, "proxies.json");
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "proxies.json");
    }

    public static ReverseProxyConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            return new ReverseProxyConfig { Enabled = false };
        }

        var json = File.ReadAllText(configPath);
        return System.Text.Json.JsonSerializer.Deserialize<ReverseProxyConfig>(json, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = System.Text.Json.JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        }) ?? new ReverseProxyConfig { Enabled = false };
    }

    public static ReverseProxyRoute? GetRouteById(string configPath, string id)
    {
        lock (SyncRoot)
        {
            var config = Load(configPath);
            return config.Routes.FirstOrDefault(route => string.Equals(route.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }
}
