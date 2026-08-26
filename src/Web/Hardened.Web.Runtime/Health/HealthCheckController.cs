using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Health;

/// <summary>
/// Answers the two probes.
/// </summary>
/// <remarks>
/// <para>
/// A controller rather than the provider answering inline, so a probe has the same shape as any
/// other handler - which is what <c>ExecutionHelper</c> is built to run, and what makes the filter
/// chain, conventions and authorization apply to it unchanged. The same reason
/// <c>OpenApiUiController</c>, <c>OpenApiDocumentController</c> and <c>StaticContentController</c>
/// exist.
/// </para>
/// <para>
/// Stateless per request and a singleton for that reason. The checks are resolved per probe rather
/// than held, so one registered as scoped or transient behaves as its registration says rather than
/// being pinned to the first probe.
/// </para>
/// </remarks>
public class HealthCheckController {
    private readonly IServiceProvider _rootProvider;
    private readonly HealthCheckConfiguration _config;

    public HealthCheckController(IServiceProvider rootProvider, HealthCheckConfiguration config) {
        _rootProvider = rootProvider;
        _config = config;
    }

    /// <summary>
    /// Alive if this code is running. No dependency is consulted, on purpose.
    /// </summary>
    public Task Live(IExecutionContext context) {
        Write(context, 200, HealthStatus.Healthy, Array.Empty<(string, HealthCheckResult)>());

        return Task.CompletedTask;
    }

    public async Task Ready(IExecutionContext context) {
        // Resolved per probe rather than held, so a check registered as scoped or transient behaves
        // as its registration says rather than being pinned to the first probe.
        var checks = _rootProvider.GetServices<IHealthCheck>().ToArray();

        if (checks.Length == 0) {
            // No checks is ready. An application that registered none has not said it is unhealthy;
            // it has said it has nothing to verify.
            Write(context, 200, HealthStatus.Healthy, Array.Empty<(string, HealthCheckResult)>());

            return;
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);

        budget.CancelAfter(_config.TotalTimeout);

        var results = await Task.WhenAll(checks.Select(check => Run(check, budget.Token)));

        var worst = results.Aggregate(
            HealthStatus.Healthy, (current, result) => Max(current, result.Item2.Status));

        Write(context, worst == HealthStatus.Unhealthy ? 503 : 200, worst, results);
    }

    /// <summary>
    /// One check, bounded, with a failure of its own turned into a verdict rather than an exception.
    /// </summary>
    /// <remarks>
    /// A check that throws is unhealthy - that is what "I could not reach it" looks like from
    /// inside - and a check that overruns its timeout is unhealthy for the same reason. Neither is
    /// allowed to fail the probe itself, because a 500 from a readiness endpoint is far less
    /// actionable than a 503.
    /// </remarks>
    private async Task<(string, HealthCheckResult)> Run(IHealthCheck check, CancellationToken budget) {
        using var perCheck = CancellationTokenSource.CreateLinkedTokenSource(budget);

        perCheck.CancelAfter(_config.CheckTimeout);

        try {
            return (check.Name, await check.Check(perCheck.Token));
        }
        catch (OperationCanceledException) {
            return (check.Name, HealthCheckResult.Unhealthy("timed out"));
        }
        catch (Exception exception) {
            return (check.Name, HealthCheckResult.Unhealthy(exception.GetType().Name));
        }
    }

    private static HealthStatus Max(HealthStatus left, HealthStatus right) =>
        left > right ? left : right;

    /// <summary>
    /// Writes the body directly rather than going through serialization.
    /// </summary>
    /// <remarks>
    /// The same reason <c>OpenApiDocumentController</c> does: there is nothing to negotiate, and
    /// leaving <c>ShouldSerialize</c> on would have the locator pick a serializer from whatever the
    /// prober happened to send in <c>Accept</c>. It also keeps the endpoint answerable when the
    /// serialization stack is itself the thing that is broken.
    /// </remarks>
    private void Write(
        IExecutionContext context,
        int status,
        HealthStatus overall,
        IReadOnlyCollection<(string Name, HealthCheckResult Result)> results) {
        var response = context.Response;

        response.Status = status;
        response.ContentType = "application/json";
        response.ShouldSerialize = false;
        response.Headers[KnownHeaders.CacheControl] = new StringValues("no-store");

        using var writer = new Utf8JsonWriter(response.Body);

        writer.WriteStartObject();
        writer.WriteString("status", overall.ToString());

        if (_config.IncludeDetail && results.Count > 0) {
            writer.WriteStartObject("checks");

            foreach (var (name, result) in results) {
                writer.WriteStartObject(name);
                writer.WriteString("status", result.Status.ToString());

                if (result.Description != null) {
                    writer.WriteString("description", result.Description);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.Flush();
    }
}
