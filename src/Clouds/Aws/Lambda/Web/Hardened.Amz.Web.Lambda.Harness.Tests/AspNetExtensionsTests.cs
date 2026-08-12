using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Web.Lambda.Harness;
using Hardened.Amz.Web.Lambda.Runtime.Impl;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Harness.Tests;

/// <summary>
/// The two lines a consumer writes to host a Lambda web application behind Kestrel.
/// </summary>
public class AspNetExtensionsTests {

    private class NoopHandler : IApiGatewayV2Handler {
        public Task<APIGatewayHttpApiV2ProxyResponse> Invoke(
            APIGatewayHttpApiV2ProxyRequest request, ILambdaContext context) =>
            Task.FromResult(new APIGatewayHttpApiV2ProxyResponse());
    }

    [Fact]
    public void RegisteringAnApplicationResolvesTheRequestService() {
        var services = new ServiceCollection();

        services.AddLambdaApplication<NoopHandler>();

        using var provider = services.BuildServiceProvider();

        Assert.IsType<RequestToLambdaService<NoopHandler>>(
            provider.GetRequiredService<IRequestToLambdaService>());
    }

    /// <summary>
    /// The service constructs the application once and reuses it. A per-request registration would
    /// rebuild the whole DI container and re-run startup on every call.
    /// </summary>
    [Fact]
    public void TheRequestServiceIsASingleton() {
        var services = new ServiceCollection();

        services.AddLambdaApplication<NoopHandler>();

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<IRequestToLambdaService>(),
            provider.GetRequiredService<IRequestToLambdaService>());
    }
}
