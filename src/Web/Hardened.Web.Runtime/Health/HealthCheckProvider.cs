using System.Text.Json;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace Hardened.Web.Runtime.Health;

/// <summary>
/// Serves liveness and readiness at fixed paths.
/// </summary>
/// <remarks>
/// <para>
/// Registered as one more <c>IWebExecutionRequestHandlerProvider</c>, which is how routing tables
/// themselves are registered and how <c>OpenApiDocumentProvider</c> serves its document - so this
/// needs no route attribute and no entry in any generated table. Providers are consulted in reverse
/// registration order, so an application that declares its own <c>/health/ready</c> wins.
/// </para>
/// <para>
/// <b>What this bypasses, exactly.</b> <c>WebExecutionHandlerService</c> is itself the last
/// middleware, so everything registered through <c>IMiddlewareService</c> - CORS, a global rate
/// limiter - still runs before a probe reaches here. What is skipped is the per-handler filter set
/// from <c>IGlobalFilterRegistry</c>, because this builds its own chain. That is the intended split:
/// a probe must not need a credential, and with default-deny authorization there is no attribute
/// here to hang an exemption on.
/// </para>
/// <para>
/// <b>Not for Lambda.</b> There is no pool to drain and no orchestrator polling, so nothing should
/// register this in a Lambda module.
/// </para>
/// </remarks>
public class HealthCheckProvider : IWebExecutionRequestHandlerProvider {
    private readonly HealthCheckConfiguration _config;
    private readonly IServiceProvider _rootProvider;
    private readonly IExecutionRequestHandler _live;
    private readonly IExecutionRequestHandler _ready;

    public HealthCheckProvider(HealthCheckConfiguration config, IServiceProvider rootProvider) {
        _config = config;
        _rootProvider = rootProvider;

        _live = new Handler(_config.LivePath, Live);
        _ready = new Handler(_config.ReadyPath, Ready);
    }

    public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context) {
        var method = context.Request.Method;

        // HEAD as well as GET: a probe issuing HEAD is asking the same question, and the routing
        // table's usual HEAD-to-GET redirection does not apply to a provider serving its own chain.
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        var path = context.Request.Path;

        if (string.Equals(path, _config.LivePath, StringComparison.Ordinal)) {
            return new RequestHandlerInfo(_live, PathTokenCollection.Empty);
        }

        if (string.Equals(path, _config.ReadyPath, StringComparison.Ordinal)) {
            return new RequestHandlerInfo(_ready, PathTokenCollection.Empty);
        }

        return null;
    }

    /// <summary>
    /// Alive if this code is running. No dependency is consulted, on purpose.
    /// </summary>
    private Task Live(IExecutionContext context) {
        Write(context, 200, HealthStatus.Healthy, Array.Empty<(string, HealthCheckResult)>());

        return Task.CompletedTask;
    }

    private async Task Ready(IExecutionContext context) {
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
    /// The same reason <c>OpenApiDocumentProvider</c> does: there is nothing to negotiate, and
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

    /// <summary>
    /// A handler whose chain is one filter: the thing that answers.
    /// </summary>
    private sealed class Handler : IExecutionRequestHandler {
        private readonly Func<IExecutionContext, Task> _answer;

        public Handler(string path, Func<IExecutionContext, Task> answer) {
            _answer = answer;

            HandlerInfo = new ExecutionRequestHandlerInfo(
                path, "GET", typeof(HealthCheckProvider), nameof(GetExecutionRequestHandler));
        }

        public IExecutionRequestHandlerInfo HandlerInfo { get; }

        public IExecutionChain GetExecutionChain(IExecutionContext context) =>
            new ExecutionChain(
                new Func<IExecutionContext, IExecutionFilter>[] { _ => new AnswerFilter(_answer) },
                context);
    }

    private sealed class AnswerFilter : IExecutionFilter {
        private readonly Func<IExecutionContext, Task> _answer;

        public AnswerFilter(Func<IExecutionContext, Task> answer) {
            _answer = answer;
        }

        public Task Execute(IExecutionChain chain) => _answer(chain.Context);
    }
}
