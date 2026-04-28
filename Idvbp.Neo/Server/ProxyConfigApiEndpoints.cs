using Idvbp.Neo.Server.Middleware;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idvbp.Neo.Server;

public static class ProxyConfigApiEndpoints
{
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

public sealed record UpdateProxyPageConfigRequest(string? Value);
