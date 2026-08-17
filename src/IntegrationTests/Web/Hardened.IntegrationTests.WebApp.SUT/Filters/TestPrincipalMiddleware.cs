using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Middleware;
using Hardened.Shared.Runtime.Application;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.WebApp.SUT.Filters;

/// <summary>
/// Stands in for authentication, which the framework does not ship yet.
/// </summary>
/// <remarks>
/// <para>
/// Reads a header and puts a principal on the context, so the authorization tests can exercise a
/// caller who holds grants as well as one who holds none. When real token validation arrives this
/// is what it replaces - the seam it uses, a settable principal on the context, is the production
/// one.
/// </para>
/// <para>
/// Middleware rather than a request filter, so it runs ahead of the whole handler chain and both
/// authorization positions see the same principal.
/// </para>
/// </remarks>
public class TestPrincipalMiddleware : IExecutionFilter {
    public const string GrantsHeader = "X-Test-Grants";

    public Task Execute(IExecutionChain chain) {
        var context = chain.Context;

        if (context.Request.Headers.TryGetValue(GrantsHeader, out var grants)) {
            context.CallerPrincipal = new CallerPrincipal(
                "test",
                grants.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries),
                subject: "integration-test");
        }

        return chain.Next();
    }
}

internal class TestPrincipalStartupService : IStartupService {
    public Task<bool> Startup(IServiceProvider rootProvider) {
        rootProvider.GetRequiredService<IMiddlewareService>()
            .Use(_ => new TestPrincipalMiddleware());

        return Task.FromResult(true);
    }
}
