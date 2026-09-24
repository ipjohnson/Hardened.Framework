using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Cors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Cors;

/// <summary>
/// What <c>[Cors]</c> installs on a route: one filter, from the nearest declaration, answering with
/// the policy that declaration names.
/// </summary>
public class CorsAttributeTests
{
    private const string Allowed = "https://app.example.com";
    private const string Partner = "https://partner.example.com";

    private sealed class Partners;

    private sealed class Unregistered;

    private static IExecutionRequestHandlerInfo Info(params object[] metadata) =>
        new ExecutionRequestHandlerInfo(
            "/orders",
            "GET",
            typeof(CorsAttributeTests),
            nameof(Info),
            metadata: metadata
        );

    /// <summary>
    /// The default policy allows <see cref="Allowed"/>, and the <see cref="Partners"/> policy allows
    /// <see cref="Partner"/> only.
    /// </summary>
    private static IServiceProvider Services()
    {
        var services = new ServiceCollection();
        var defaultPolicy = new CorsConfiguration();

        defaultPolicy.AllowOrigin(Allowed);

        services.AddSingleton(defaultPolicy);
        services.AddCorsPolicy<Partners>(policy => policy.AllowOrigin(Partner));

        return services.BuildServiceProvider();
    }

    private static IExecutionContext Context(IServiceProvider services, string? origin)
    {
        var headers = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);

        if (origin != null)
        {
            headers[KnownHeaders.Origin] = origin;
        }

        var request = new TestExecutionRequest(
            "GET",
            "/orders",
            "application/json",
            new SimpleQueryStringCollection(new Dictionary<string, string>())
        )
        {
            Headers = headers,
        };

        return new TestExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            request,
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );
    }

    /// <summary>
    /// Runs the one filter <paramref name="declaration"/> installs, and returns the response
    /// headers it left.
    /// </summary>
    private static async Task<IDictionary<string, StringValues>> Run(
        CorsAttribute declaration,
        IExecutionRequestHandlerInfo info,
        string? origin
    )
    {
        var installed = Assert.Single(declaration.GetFilters(info));
        var context = Context(Services(), origin);
        var continued = false;

        var chain = new ExecutionChain(
            new Func<IExecutionContext, IExecutionFilter>[]
            {
                installed.FilterFunc,
                _ => new Terminal(() => continued = true),
            },
            context
        );

        await chain.Next();

        Assert.True(continued, "The route's CORS filter stopped the chain.");

        return context.Response.Headers;
    }

    private sealed class Terminal : IExecutionFilter
    {
        private readonly Action _onRun;

        public Terminal(Action onRun)
        {
            _onRun = onRun;
        }

        public Task Execute(IExecutionChain chain)
        {
            _onRun();

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task AnAllowedOriginIsAnsweredWithTheDefaultPolicy()
    {
        var declaration = new CorsAttribute();

        var headers = await Run(declaration, Info(declaration), Allowed);

        Assert.Equal(Allowed, headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
        Assert.Contains(KnownHeaders.Origin, headers[KnownHeaders.Vary].ToString());
    }

    /// <summary>
    /// Refused by carrying no allow header, and still marked as varying by origin, because the
    /// answer was decided by it.
    /// </summary>
    [Fact]
    public async Task AnOriginThePolicyDoesNotAllowGetsNoAllowHeader()
    {
        var declaration = new CorsAttribute();

        var headers = await Run(declaration, Info(declaration), Partner);

        Assert.False(headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
        Assert.Contains(KnownHeaders.Origin, headers[KnownHeaders.Vary].ToString());
    }

    [Fact]
    public async Task ARequestWithNoOriginIsLeftAlone()
    {
        var declaration = new CorsAttribute();

        var headers = await Run(declaration, Info(declaration), origin: null);

        Assert.False(headers.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
        Assert.False(headers.ContainsKey(KnownHeaders.Vary));
    }

    [Fact]
    public async Task ANamedPolicyAnswersWithItsOwnOrigins()
    {
        var declaration = new CorsAttribute<Partners>();

        var partner = await Run(declaration, Info(declaration), Partner);
        var allowedByTheDefault = await Run(declaration, Info(declaration), Allowed);

        Assert.Equal(Partner, partner[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
        Assert.False(allowedByTheDefault.ContainsKey(KnownHeaders.Cors.AccessControlAllowOrigin));
    }

    /// <summary>
    /// On the route's first request, which is when its filter is built. The message names the
    /// policy, because the attribute is what the developer has to go and find.
    /// </summary>
    [Fact]
    public void APolicyNothingRegisteredFailsWithItsName()
    {
        var declaration = new CorsAttribute<Unregistered>();
        var installed = Assert.Single(declaration.GetFilters(Info(declaration)));

        var failure = Assert.Throws<InvalidOperationException>(() =>
            installed.FilterFunc(Context(Services(), Allowed))
        );

        Assert.Contains("Cors<Unregistered>", failure.Message);
        Assert.Contains("AddCorsPolicy<Unregistered>", failure.Message);
    }

    /// <summary>
    /// A method's declaration comes first in the metadata, so its class's installs nothing. Two
    /// filters would each write the allow header, and the later one would decide it.
    /// </summary>
    [Fact]
    public async Task OnlyTheNearestDeclarationInstallsAFilter()
    {
        var method = new CorsAttribute<Partners>();
        var type = new CorsAttribute();
        var info = Info(method, type);

        Assert.Empty(type.GetFilters(info));

        var headers = await Run(method, info, Partner);

        Assert.Equal(Partner, headers[KnownHeaders.Cors.AccessControlAllowOrigin].ToString());
    }

    /// <summary>
    /// A module's declaration is appended to the handler's metadata as its chain is built. It
    /// installs the filter where the handler declares nothing, and stands down where it does.
    /// </summary>
    [Fact]
    public void TheModulesDeclarationReachesAHandlerThatDeclaresNone()
    {
        var module = new CorsAttribute();

        Assert.Single(module.GetFilters(Info().WithWiderRungs([module])));
        Assert.Empty(
            module.GetFilters(Info(new CorsAttribute<Partners>()).WithWiderRungs([module]))
        );
    }

    /// <summary>
    /// Ahead of the first stage that can refuse a request, so a refusal still carries the headers a
    /// browser needs before a script may read it.
    /// </summary>
    [Fact]
    public void TheFilterRunsAheadOfEveryRefusal()
    {
        var declaration = new CorsAttribute();
        var installed = Assert.Single(declaration.GetFilters(Info(declaration)));

        Assert.True(installed.Order < FilterOrder.RateLimitTransport);
        Assert.True(installed.Order > FilterOrder.HandlerCreation);
    }
}
