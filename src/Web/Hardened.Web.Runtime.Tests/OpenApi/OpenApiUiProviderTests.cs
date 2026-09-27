using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Requests.Runtime.Filters;
using Hardened.Requests.Runtime.QueryString;
using Hardened.Requests.Testing;
using Hardened.Web.Runtime.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Hardened.Web.Runtime.Tests.OpenApi;

/// <summary>
/// The reference page follows the document: public under default-deny where the document is.
/// </summary>
public class OpenApiUiProviderTests
{
    private sealed class PassThrough : IExecutionFilter
    {
        public Task Execute(IExecutionChain chain) => chain.Next();
    }

    private static readonly IOpenApiUiConfiguration Page = new OpenApiUiConfiguration(
        "/docs",
        "Docs",
        "/openapi.json",
        "https://cdn.example/ui.js",
        null
    );

    private static IExecutionRequestHandlerInfo HandlerInfo(Action<IServiceCollection>? configure)
    {
        var collection = new ServiceCollection();

        var ioProvider = Substitute.For<IIOFilterProvider>();
        ioProvider
            .ProvideFilter(
                Arg.Any<IExecutionRequestHandlerInfo>(),
                Arg.Any<Func<IExecutionContext, Task<IExecutionRequestParameters>>>()
            )
            .Returns(new PassThrough());

        collection.AddSingleton(ioProvider);
        collection.AddSingleton<IInstanceFilterProvider, InstanceFilterProvider>();
        collection.AddSingleton<IGlobalFilterRegistry>(
            new GlobalFilterRegistry(Array.Empty<IRequestFilterProvider>())
        );
        collection.AddSingleton<OpenApiUiController>();

        configure?.Invoke(collection);

        using var services = collection.BuildServiceProvider();

        var context = new TestExecutionContext(
            services,
            services,
            Substitute.For<IKnownServices>(),
            new TestExecutionRequest(
                "GET",
                "/docs",
                "text/html",
                new SimpleQueryStringCollection(new Dictionary<string, string>())
            ),
            new TestExecutionResponse(new MemoryStream()),
            CancellationToken.None
        );

        var match = new OpenApiUiProvider(Page, services).Match(context);

        Assert.NotNull(match);

        return match!.Handler!.HandlerInfo;
    }

    [Fact]
    public void AllowAnonymousOnTheDocumentOpensThePage()
    {
        var handlerInfo = HandlerInfo(collection =>
            collection.AddSingleton(new OpenApiDocumentConfiguration { AllowAnonymous = true })
        );

        Assert.Contains(handlerInfo.Metadata, item => item is AllowAnonymousAttribute);
    }

    [Fact]
    public void WithoutTheSettingThePageKeepsThePosture()
    {
        Assert.DoesNotContain(HandlerInfo(null).Metadata, item => item is AllowAnonymousAttribute);
    }
}
