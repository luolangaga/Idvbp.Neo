using Idvbp.Neo.Server.Middleware;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idvbp.Neo.Server;

/// <summary>
/// 代理配置相关 API 端点定义。
/// </summary>
public static class ProxyConfigApiEndpoints
{
    /// <summary>
    /// 映射代理配置相关的 REST API 端点。
    /// </summary>
    /// <param name="endpoints">端点路由构建器。</param>
    public static void MapProxyConfigApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/proxies/{id}/page-config", (string id, IProxyPageConfigRepository repository) =>
        {
            var route = ReverseProxyConfigLoader.GetRouteById(ReverseProxyConfigLoader.ResolveConfigPath(), id);
            return route is null
                ? Results.NotFound(new { message = $"Proxy route '{id}' not found." })
                : Results.Ok(new { id = route.Id, value = repository.GetValueOrDefault(route.Id) });
        });

        endpoints.MapPut("/api/proxies/{id}/page-config", (string id, UpdateProxyPageConfigRequest request, IProxyPageConfigRepository repository) =>
        {
            var route = ReverseProxyConfigLoader.GetRouteById(ReverseProxyConfigLoader.ResolveConfigPath(), id);
            if (route is null)
            {
                return Results.NotFound(new { message = $"Proxy route '{id}' not found." });
            }

            repository.Upsert(route.Id, request.Value ?? string.Empty);
            return Results.Ok(new { id = route.Id, value = request.Value ?? string.Empty });
        });
    }
}

/// <summary>
/// 更新代理页面配置请求记录。
/// </summary>
public sealed record UpdateProxyPageConfigRequest(string? Value);
