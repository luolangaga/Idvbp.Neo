using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Idvbp.Neo.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Idvbp.Neo.Server;

public static class FrontendPackageApiEndpoints
{
    public static void MapFrontendPackageApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/frontends", (IFrontendPackageService service) =>
            Results.Ok(service.GetPackages()));

        endpoints.MapGet("/api/frontends/{id}", (string id, IFrontendPackageService service) =>
        {
            var package = service.GetPackage(id);
            return package is null
                ? Results.NotFound(new { message = $"Frontend package '{id}' not found." })
                : Results.Ok(package);
        });

        endpoints.MapGet("/api/frontends/components", (IFrontendPackageService service) =>
            Results.Ok(service.GetComponents()));

        endpoints.MapGet("/api/frontends/{id}/pages/{pageId}/designer-components", (
            string id,
            string pageId,
            IFrontendPackageService service) =>
        {
            try
            {
                return Results.Ok(service.GetDesignerComponentInstances(id, pageId));
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapGet("/api/frontends/fonts", (IFrontendPackageService service) =>
            Results.Ok(service.GetFonts()));

        endpoints.MapPost("/api/frontends/fonts/import", async (
            IFormFile file,
            IFrontendPackageService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ImportFontAsync(file, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        }).DisableAntiforgery();

        endpoints.MapPost("/api/frontends/{id}/components/import", async (
            string id,
            ImportFrontendComponentRequest request,
            IFrontendPackageService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ImportComponentAsync(id, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapPost("/api/frontends/{id}/components/designer", async (
            string id,
            DesignerComponentCreateRequest request,
            IFrontendPackageService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.CreateDesignerComponentAsync(id, request, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });

        endpoints.MapPost("/api/frontends/{id}/assets/import", async (
            string id,
            HttpRequest request,
            IFrontendPackageService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var file = request.Form.Files.FirstOrDefault();
                if (file is null)
                {
                    return Results.BadRequest(new { message = "Asset file is required." });
                }

                var category = request.Form["category"].FirstOrDefault() ?? "assets";
                return Results.Ok(await service.ImportAssetAsync(id, file, category, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        }).DisableAntiforgery();

        endpoints.MapGet("/api/frontends/{id}/pages/{pageId}/config", (
            string id,
            string pageId,
            IFrontendPackageService service,
            IProxyPageConfigRepository repository) =>
        {
            var package = service.GetPackage(id);
            if (package is null)
            {
                return Results.NotFound(new { message = $"Frontend package '{id}' not found." });
            }

            if (!package.Pages.Any(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound(new { message = $"Frontend page '{pageId}' not found." });
            }

            var configKey = BuildFrontendConfigKey(id, pageId);
            return Results.Ok(new { id = configKey, value = repository.GetValueOrDefault(configKey) });
        });

        endpoints.MapGet("/api/frontends/{id}/pages/{pageId}/components/config", (
            string id,
            string pageId,
            IFrontendPackageService service,
            IProxyPageConfigRepository repository) =>
        {
            var package = service.GetPackage(id);
            if (package is null)
            {
                return Results.NotFound(new { message = $"Frontend package '{id}' not found." });
            }

            if (!package.Pages.Any(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound(new { message = $"Frontend page '{pageId}' not found." });
            }

            var prefix = BuildFrontendComponentConfigPrefix(id, pageId);
            var values = repository.GetAll()
                .Where(item => item.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    item => item.Key[prefix.Length..],
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase);
            return Results.Ok(new { values });
        });

        endpoints.MapGet("/api/frontends/{id}/pages/{pageId}/components/{componentId}/config", (
            string id,
            string pageId,
            string componentId,
            IFrontendPackageService service,
            IProxyPageConfigRepository repository) =>
        {
            var package = service.GetPackage(id);
            if (package is null)
            {
                return Results.NotFound(new { message = $"Frontend package '{id}' not found." });
            }

            if (!package.Pages.Any(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound(new { message = $"Frontend page '{pageId}' not found." });
            }

            var configKey = BuildFrontendComponentConfigKey(id, pageId, componentId);
            return Results.Ok(new { id = configKey, value = repository.GetValueOrDefault(configKey) });
        });

        endpoints.MapPut("/api/frontends/{id}/pages/{pageId}/config", (
            string id,
            string pageId,
            UpdateFrontendPageConfigRequest request,
            IFrontendPackageService service,
            IProxyPageConfigRepository repository) =>
        {
            var package = service.GetPackage(id);
            if (package is null)
            {
                return Results.NotFound(new { message = $"Frontend package '{id}' not found." });
            }

            if (!package.Pages.Any(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound(new { message = $"Frontend page '{pageId}' not found." });
            }

            var configKey = BuildFrontendConfigKey(id, pageId);
            repository.Upsert(configKey, request.Value ?? string.Empty);
            return Results.Ok(new { id = configKey, value = request.Value ?? string.Empty });
        });

        endpoints.MapPut("/api/frontends/{id}/pages/{pageId}/components/{componentId}/config", (
            string id,
            string pageId,
            string componentId,
            UpdateFrontendPageConfigRequest request,
            IFrontendPackageService service,
            IProxyPageConfigRepository repository) =>
        {
            var package = service.GetPackage(id);
            if (package is null)
            {
                return Results.NotFound(new { message = $"Frontend package '{id}' not found." });
            }

            if (!package.Pages.Any(page => string.Equals(page.Id, pageId, StringComparison.OrdinalIgnoreCase)))
            {
                return Results.NotFound(new { message = $"Frontend page '{pageId}' not found." });
            }

            var configKey = BuildFrontendComponentConfigKey(id, pageId, componentId);
            repository.Upsert(configKey, request.Value ?? string.Empty);
            return Results.Ok(new { id = configKey, value = request.Value ?? string.Empty });
        });

        endpoints.MapPost("/api/frontends/import", async (IFormFile file, IFrontendPackageService service, CancellationToken cancellationToken) =>
        {
            try
            {
                return Results.Ok(await service.ImportAsync(file, cancellationToken));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
        }).DisableAntiforgery();

        endpoints.MapGet("/api/frontends/{id}/package", async (string id, IFrontendPackageService service, HttpContext context, CancellationToken cancellationToken) =>
        {
            try
            {
                context.Response.ContentType = "application/zip";
                context.Response.Headers.ContentDisposition = $"attachment; filename=\"{id}.zip\"";
                await service.WritePackageZipAsync(id, context.Response.Body, cancellationToken);
            }
            catch (KeyNotFoundException exception)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsJsonAsync(new { message = exception.Message }, cancellationToken);
            }
        });

        endpoints.MapPut("/api/frontends/{id}/layout", async (
            string id,
            string path,
            JsonElement layout,
            IFrontendPackageService service,
            CancellationToken cancellationToken) =>
        {
            try
            {
                await service.SaveLayoutAsync(id, path, layout, cancellationToken);
                return Results.Ok(new { saved = true });
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return Results.BadRequest(new { message = exception.Message });
            }
            catch (KeyNotFoundException exception)
            {
                return Results.NotFound(new { message = exception.Message });
            }
        });
    }

    private static string BuildFrontendConfigKey(string packageId, string pageId)
        => $"frontend:{packageId}:{pageId}";

    private static string BuildFrontendComponentConfigPrefix(string packageId, string pageId)
        => $"{BuildFrontendConfigKey(packageId, pageId)}:component:";

    private static string BuildFrontendComponentConfigKey(string packageId, string pageId, string componentId)
        => $"{BuildFrontendComponentConfigPrefix(packageId, pageId)}{componentId}";
}

public sealed record UpdateFrontendPageConfigRequest(string? Value);
