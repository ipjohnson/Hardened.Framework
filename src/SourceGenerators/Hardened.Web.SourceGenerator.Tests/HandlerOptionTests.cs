using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.CacheControl;
using Hardened.Web.SourceGenerator.Tests.Routing;
using Microsoft.Extensions.Primitives;
using NSubstitute;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The three attributes that change what a handler produces rather than where it is reachable:
/// <c>[RawResponse]</c> replaces the default output function,
/// <c>[CacheControl]</c> travels as handler metadata.
///
/// <para>
/// Each is asserted through the compiled, loaded handler rather than against the emitted string —
/// the generator building the right call and the runtime doing anything with it are two different
/// claims, and only the second one is worth a consumer's time.
/// </para>
/// </summary>
public class HandlerOptionTests {




    /// <summary>
    /// <c>[CacheControl]</c> is not read by the generator as an output option — it travels as
    /// handler metadata, which is where a filter reading it has to find it. Its values have to
    /// survive the trip, or a route declaring a one-day cache is served with the attribute's
    /// defaults.
    /// </summary>
    [Fact]
    public void CacheControlReachesTheHandlerMetadataWithItsMaxAge() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class AssetController {
                [Get("/assets/{name}")]
                [CacheControl(MaxAge = 86400)]
                public string Asset(string name) => name;
            }
            """);

        var metadata = routing.Handler("GET", "/assets/logo.png").Metadata;

        var cacheControl = Assert.IsType<CacheControlAttribute>(Assert.Single(metadata));

        Assert.Equal(86400, cacheControl.MaxAge);
    }

    /// <summary>
    /// The <c>Type</c> flags survive the trip too.
    ///
    /// <para>
    /// The enum is written fully qualified here on purpose. A property assignment on a handler
    /// attribute is copied verbatim from the consumer's source into the generated file, which
    /// carries none of the consumer's <c>using</c> directives — so the natural spelling,
    /// <c>Type = CacheControlEnum.MaxAge | CacheControlEnum.Public</c> under
    /// <c>using Hardened.Web.Runtime.CacheControl;</c>, emits an unqualified name and fails with
    /// CS0103. Found 2026-08-11 by this suite; the defect is in the shared
    /// <c>HandlerInfoCodeGenerator</c> metadata emit, not in the web generator, so it is reported
    /// rather than worked around here.
    /// </para>
    /// </summary>
    [Fact]
    public void CacheControlFlagsReachTheHandlerMetadata() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class AssetController {
                [Get("/assets/{name}")]
                [CacheControl(Type = global::Hardened.Web.Runtime.CacheControl.CacheControlEnum.NoStore)]
                public string Asset(string name) => name;
            }
            """);

        var metadata = routing.Handler("GET", "/assets/logo.png").Metadata;

        var cacheControl = Assert.IsType<CacheControlAttribute>(Assert.Single(metadata));

        Assert.Equal(CacheControlEnum.NoStore, cacheControl.Type);
    }

    /// <summary>
    /// A handler with no attributes beyond its verb carries no metadata. The empty case matters
    /// because the metadata array and the parameter array are both optional constructor arguments
    /// in the same position order — a handler with metadata and no parameters put the metadata in
    /// the parameters slot and emitted code that did not compile, before the 2026-08-11 fix.
    /// </summary>
    [Fact]
    public void AHandlerWithNoAttributesCarriesNoMetadata() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class HealthController {
                [Get("/health")]
                public string Health() => "ok";
            }
            """);

        Assert.Empty(routing.Handler("GET", "/health").Metadata);
    }

    /// <summary>
    /// <c>[CacheControl]</c> on the controller applies to every route on it. Class-level filter
    /// attributes are collected alongside the method's own.
    /// </summary>
    [Fact]
    public void CacheControlOnTheControllerReachesEveryHandlerOnIt() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [CacheControl(MaxAge = 60)]
            public class AssetController {
                [Get("/one")]
                public string One() => "one";

                [Get("/two")]
                public string Two() => "two";
            }
            """);

        foreach (var path in new[] { "/one", "/two" }) {
            var cacheControl = Assert.IsType<CacheControlAttribute>(
                Assert.Single(routing.Handler("GET", path).Metadata));

            Assert.Equal(60, cacheControl.MaxAge);
        }
    }

    /// <summary>
    /// A response context with a value already set, so a default output function can be invoked
    /// directly. Only the members the raw output path touches are stood up.
    /// </summary>
    private static (IExecutionContext context, MemoryStream body) RawResponseContext(string responseValue) {
        var context = Substitute.For<IExecutionContext>();
        var response = Substitute.For<IExecutionResponse>();
        var body = new MemoryStream();

        // Read-write properties on a substitute remember what is written to them, so the output
        // function's assignment to ContentType is observable without recording it by hand.
        response.ResponseValue = responseValue;
        response.Body = body;
        response.Headers.Returns(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase));

        context.Response.Returns(response);

        return (context, body);
    }
}
