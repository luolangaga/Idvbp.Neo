using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Idvbp.Neo.Server.Middleware;

public class ReverseProxyOptions
{
    public string TargetUrl { get; set; } = "http://localhost:5173";
    public string PathPrefix { get; set; } = "/proxy";
}

public class ReverseProxyMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ReverseProxyOptions _options;
    private readonly HttpClient _httpClient;

    public ReverseProxyMiddleware(RequestDelegate next, ReverseProxyOptions options)
    {
        _next = next;
        _options = options;
        _httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = true
        });
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments(_options.PathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var targetPath = context.Request.Path.Value;
        if (targetPath != null)
        {
            targetPath = targetPath[_options.PathPrefix.Length..];
            if (!targetPath.StartsWith('/'))
                targetPath = "/" + targetPath;
        }

        var targetUrl = $"{_options.TargetUrl.TrimEnd('/')}{targetPath}{context.Request.QueryString}";

        try
        {
            var requestMessage = new HttpRequestMessage
            {
                Method = new HttpMethod(context.Request.Method),
                RequestUri = new Uri(targetUrl)
            };

            foreach (var header in context.Request.Headers)
            {
                if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (header.Key.Equals("Origin", StringComparison.OrdinalIgnoreCase))
                    continue;
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            if (context.Request.ContentLength > 0)
            {
                requestMessage.Content = new StreamContent(context.Request.Body);
                if (!string.IsNullOrEmpty(context.Request.ContentType))
                    requestMessage.Content.Headers.ContentType =
                        System.Net.Http.Headers.MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }

            using var response = await _httpClient.SendAsync(
                requestMessage,
                HttpCompletionOption.ResponseHeadersRead,
                context.RequestAborted);

            context.Response.StatusCode = (int)response.StatusCode;

            foreach (var header in response.Headers)
            {
                if (header.Key.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase))
                    continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            foreach (var header in response.Content.Headers)
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
        }
        catch (HttpRequestException)
        {
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync("502 Bad Gateway - Proxy target unreachable");
        }
        catch (TaskCanceledException)
        {
            context.Response.StatusCode = 504;
            await context.Response.WriteAsync("504 Gateway Timeout");
        }
    }
}
