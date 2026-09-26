using System.Text;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Execution;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.Handlers;
using Hardened.Web.Runtime.OpenApi;
using Hardened.Web.Runtime.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.Routing;

/// <summary>
/// What startup does with the document once the routes registered at run time are in the table:
/// writes them into it where there is a document to write them into, and leaves it as the build
/// wrote it everywhere else.
/// </summary>
public class RouteRegistrationStartupServiceTests
{
    private const string Prefix = "{\"openapi\":\"3.1.0\",\"paths\":{";

    private const string Suffix = "},\"components\":{}}";

    private const string Compiled = Prefix + Suffix;

    private const string Operation = "\"get\":{\"summary\":\"orders\"}";

    [Fact]
    public async Task ARegisteredRouteIsWrittenIntoTheServedDocument()
    {
        var services = Services(new Catalog(Prefix, Suffix), Operation);

        await new RouteRegistrationStartupService().Startup(services);

        Assert.Equal(
            "{\"openapi\":\"3.1.0\",\"paths\":{\"/orders\":{\"get\":{\"operationId\":\"ordersGet\",\"summary\":\"orders\"}}},\"components\":{}}",
            await Served(services)
        );
    }

    /// <summary>
    /// Without a catalog there are no compiled halves to write a route between, so the document is
    /// served as the build wrote it.
    /// </summary>
    [Fact]
    public async Task WithNoCatalogTheDocumentIsServedAsBuilt()
    {
        var services = Services(catalog: null, Operation);

        await new RouteRegistrationStartupService().Startup(services);

        Assert.Equal(Compiled, await Served(services));
    }

    /// <summary>
    /// A route from an entry point without <c>[OpenApiDocument]</c> publishes no operation, and a
    /// table of those leaves the document as the build wrote it.
    /// </summary>
    [Fact]
    public async Task RoutesThatPublishNoOperationLeaveTheDocumentAsBuilt()
    {
        var services = Services(new Catalog(Prefix, Suffix), operation: "");

        await new RouteRegistrationStartupService().Startup(services);

        Assert.Equal(Compiled, await Served(services));
    }

    /// <summary>
    /// A catalog with no document halves belongs to an application that serves no document, so an
    /// operation has nowhere to go.
    /// </summary>
    [Fact]
    public async Task ACatalogWithNoDocumentLeavesTheServedOneAsBuilt()
    {
        var services = Services(new Catalog("", ""), Operation);

        await new RouteRegistrationStartupService().Startup(services);

        Assert.Equal(Compiled, await Served(services));
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>
    /// One route registered at <c>/orders</c>, a document served from what the build compiled, and
    /// the services <c>ExecutionHelper</c> resolves to serve it.
    /// </summary>
    private static ServiceProvider Services(
        IGeneratedRouteHandlerCatalog? catalog,
        string operation
    )
    {
        var services = new ServiceCollection();

        var ioProvider = Substitute.For<IIOFilterProvider>();

        ioProvider
            .ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>()
            )
            .Returns(new PassThrough());

        services.AddSingleton(ioProvider);
        services.AddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        services.AddSingleton<IGlobalFilterRegistry>(
            new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>())
        );
        services.AddSingleton<OpenApiDocumentController>();
        services.AddSingleton(provider => new RegisteredRouteProvider(
            provider,
            expectsRegistrations: true
        ));
        services.AddSingleton<IWebExecutionRequestHandlerProvider>(
            provider => new OpenApiDocumentProvider(provider, Compiled)
        );
        services.AddSingleton<IRouteRegistration>(new RegistersOrders(operation));

        if (catalog != null)
        {
            services.AddSingleton(catalog);
        }

        return services.BuildServiceProvider();
    }

    /// <summary>The document as a client reads it, asking for no compression.</summary>
    private static async Task<string> Served(IServiceProvider services)
    {
        var provider = services
            .GetServices<IWebExecutionRequestHandlerProvider>()
            .OfType<OpenApiDocumentProvider>()
            .Single();

        var context = new TestExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            new TestExecutionRequest(
                "GET",
                provider.Path,
                "application/json",
                new SimpleQueryStringCollection(new Dictionary<string, string>())
            ),
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );

        await provider.Match(context)!.Handler!.GetExecutionChain(context).Next();

        return Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    }

    private sealed class RegistersOrders(string operation) : IRouteRegistration
    {
        public ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken)
        {
            routes.Map(
                "/orders",
                new RegisteredRouteHandler(
                    "GET",
                    [],
                    static (_, _) => Substitute.For<IExecutionRequestHandler>(),
                    operation
                )
            );

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>Stands in for the class the generator emits beside the routing table.</summary>
    private sealed class Catalog(string prefix, string suffix) : IGeneratedRouteHandlerCatalog
    {
        public IReadOnlyList<GeneratedRouteHandler> Handlers => [];

        public bool CaseInsensitiveRoutes => false;

        public string BasePath => "";

        public IReadOnlyDictionary<string, RouteConstraintTest> Constraints =>
            new Dictionary<string, RouteConstraintTest>();

        public string DocumentPrefix => prefix;

        public string DocumentSuffix => suffix;
    }

    private sealed class PassThrough : IExecutionFilter
    {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }
}
