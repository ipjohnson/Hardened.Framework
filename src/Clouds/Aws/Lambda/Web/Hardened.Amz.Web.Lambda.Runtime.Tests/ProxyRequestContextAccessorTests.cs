using Amazon.Lambda.APIGatewayEvents;
using Xunit;

namespace Hardened.Amz.Web.Lambda.Runtime.Tests;

/// <summary>
/// The accessor a handler resolves to reach the authoriser claims, the source IP and the request
/// id that API Gateway attaches to an invocation.
/// </summary>
public class ProxyRequestContextAccessorTests {

    /// <summary>
    /// The property is non-nullable on the interface but was never initialised (CS8618), so this is
    /// a singleton that promised a value and could hand back null. Anything resolving it outside an
    /// invocation — a startup service, a filter on a chain that has not been given a request —
    /// would have dereferenced it.
    /// </summary>
    [Fact]
    public void ANewAccessorHasAContextRatherThanNull() {
        IProxyRequestContextAccessor accessor = new ProxyRequestContextAccessor();

        Assert.NotNull(accessor.ProxyRequestContext);
    }

    [Fact]
    public void TheContextThatWasSetIsTheContextRead() {
        IProxyRequestContextAccessor accessor = new ProxyRequestContextAccessor();
        var context = new APIGatewayHttpApiV2ProxyRequest.ProxyRequestContext { RequestId = "req-1" };

        accessor.ProxyRequestContext = context;

        Assert.Same(context, accessor.ProxyRequestContext);
    }
}

/// <summary>
/// The attribute that marks a Lambda web application entry point.
/// </summary>
public class LambdaWebApplicationAttributeTests {

    [Fact]
    public void TheAttributeIsUsableFromAConsumersPosition() {
        Assert.True(typeof(LambdaWebApplicationAttribute).IsPublic);
        Assert.True(typeof(Attribute).IsAssignableFrom(typeof(LambdaWebApplicationAttribute)));
    }

    [Fact]
    public void TheVersionThatWasSetIsTheVersionRead() {
        var attribute = new LambdaWebApplicationAttribute { Version = ProxyIntegrationType.HttpApiV2 };

        Assert.Equal(ProxyIntegrationType.HttpApiV2, attribute.Version);
    }
}
