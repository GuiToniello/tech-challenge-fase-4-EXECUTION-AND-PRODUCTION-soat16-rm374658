using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace TechChallenge.Oficina.Execution.API.Extensions;

public static class HealthExtension
{
    // Anonimas (probes do Kubernetes e HEALTHCHECK do container). /health/ready so roda os checks com a tag "ready" (o banco).
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") }).AllowAnonymous();

        return app;
    }
}
