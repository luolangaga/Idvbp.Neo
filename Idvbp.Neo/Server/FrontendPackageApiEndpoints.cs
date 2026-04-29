using System;
using System.Collections.Generic;
using System.IO;
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
}
