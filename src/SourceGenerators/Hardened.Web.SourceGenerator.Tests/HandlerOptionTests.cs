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
/// <c>[Template]</c> and <c>[RawResponse]</c> replace the default output function,
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
    /// <c>[RawResponse]</c> installs a default output function that writes the handler's return
    /// value straight to the body under the content type it names, with no serializer in between.
    /// </summary>
    [Fact]
    public async Task RawResponseWritesTheReturnValueUnderTheDeclaredContentType() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class ExportController {
                [Get("/export")]
                [RawResponse("text/csv")]
                public string Export() => "a,b,c";
            }
            """);

        var (context, body) = RawResponseContext("a,b,c");

        routing.Route("GET", "/export")!.Handler.GetExecutionChain(context);

        Assert.NotNull(context.DefaultOutput);

        await context.DefaultOutput!(context);

        Assert.Equal("text/csv", context.Response.ContentType);
        Assert.Equal("a,b,c", System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    /// <summary>
    /// The content type is optional. Without one the attribute's own default applies, and the
    /// handler still gets a raw output function rather than the serializer.
    /// </summary>
    [Fact]
    public async Task RawResponseWithNoContentTypeDefaultsToPlainText() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class PlainController {
                [Get("/plain")]
                [RawResponse]
                public string Plain() => "plain";
            }
            """);

        var (context, _) = RawResponseContext("plain");

        routing.Route("GET", "/plain")!.Handler.GetExecutionChain(context);

        Assert.NotNull(context.DefaultOutput);

        await context.DefaultOutput!(context);

        Assert.Equal("text/plain", context.Response.ContentType);
    }

    /// <summary>
    /// A handler with no <c>[RawResponse]</c> or <c>[Template]</c> gets no default output function,
    /// which is what leaves the response to the serialization filter.
    /// </summary>
    [Fact]
    public void AHandlerWithNoOutputAttributeGetsNoDefaultOutputFunction() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class PlainController {
                [Get("/plain")]
                public string Plain() => "plain";
            }
            """);

        var (context, _) = RawResponseContext("plain");

        routing.Route("GET", "/plain")!.Handler.GetExecutionChain(context);

        Assert.Null(context.DefaultOutput);
    }

    /// <summary>
    /// <c>[Template]</c> resolves its template when the handler is constructed, not when a request
    /// arrives — so a route naming a template that does not exist fails at start-up rather than on
    /// the first request to it.
    /// </summary>
    [Fact]
    public void TemplateLooksUpTheNamedTemplateWhenTheHandlerIsBuilt() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class HomeController {
                [Get("/home")]
                [Template("home")]
                public string Home() => "home";
            }
            """);

        Assert.NotNull(routing.Route("GET", "/home"));

        routing.Templates.Received(1).FindTemplateExecutionFunction("home");
    }

    /// <summary>
    /// A template handler still gets a default output function — the one that renders through the
    /// template rather than serializing the return value.
    /// </summary>
    [Fact]
    public void TemplateInstallsADefaultOutputFunction() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class HomeController {
                [Get("/home")]
                [Template("home")]
                public string Home() => "home";
            }
            """);

        var (context, _) = RawResponseContext("home");

        routing.Route("GET", "/home")!.Handler.GetExecutionChain(context);

        Assert.NotNull(context.DefaultOutput);
    }

    /// <summary>
    /// The layout template is looked up alongside the handler's own, so a template can be wrapped
    /// without every handler naming the wrapper.
    /// </summary>
    [Fact]
    public void TemplateAlsoResolvesTheLayoutTemplate() {
        var routing = GeneratedRoutingTable.For("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class HomeController {
                [Get("/home")]
                [Template("home")]
                public string Home() => "home";
            }
            """);

        Assert.NotNull(routing.Route("GET", "/home"));

        routing.Templates.Received(1).FindTemplateExecutionFunction("_layout");
    }

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
