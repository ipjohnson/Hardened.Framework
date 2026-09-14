using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Routing;

/// <summary>
/// A route registered with a lambda, read from its call site.
/// </summary>
/// <remarks>
/// <para>
/// The generator reads everything a handler is made of except the path - the verb, the parameters,
/// the return type - emits the handler, and rewrites the call to one that carries it. The declared
/// method throws, so a call site the build did not see is loud rather than subtly different.
/// </para>
/// <para>
/// The parameters are classified by type, because there is no template to classify them against.
/// An attribute route keeps the rule it has.
/// </para>
/// </remarks>
public class LambdaRouteTests
{
    private static readonly Type[] Anchors = [typeof(GetAttribute), typeof(FromBodyAttribute)];

    private static string Application(string body) =>
        $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Routing;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public interface IClock { }

            public record Order(int Id);

            public class Routes : IRouteRegistration {
                public ValueTask Register(IRouteRegistry routes, CancellationToken cancellationToken) {
                    {{body}}

                    return default;
                }
            }
            """;

    private static GeneratorResult Generate(string body) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Application(body) },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors
        );

    private static string Emitted(string body) =>
        Generate(body).AssertNoErrors().SourceContaining("RegisteredLambdas");

    [Fact]
    public void ALambdaRegistrationEmitsAHandlerAndAnInterceptor()
    {
        var emitted = Emitted("""routes.Get("/orders/{id:int}", (int id) => id.ToString());""");

        Assert.Contains("InterceptsLocation", emitted);
        Assert.Contains(
            "IRouteRegistry routes, string path, global::System.Delegate handler",
            emitted
        );
        Assert.Contains("RegisteredRouteHandler(", emitted);
    }

    /// <remarks>
    /// Declared here because net8.0 does not have it, and <c>file</c>-scoped so an application on a
    /// framework that does, or running a second generator that emits its own, has one per file
    /// rather than a collision.
    /// </remarks>
    [Fact]
    public void TheAttributeTheCompilerReadsIsDeclared()
    {
        var emitted = Emitted("""routes.Get("/orders/{id:int}", (int id) => id.ToString());""");

        Assert.Contains("file sealed class InterceptsLocationAttribute", emitted);
    }

    /// <remarks>
    /// The lambda's natural type, written out. A <c>Delegate</c> field would mean
    /// <c>DynamicInvoke</c>, which allocates an argument array per request and does not trim.
    /// </remarks>
    [Fact]
    public void TheDelegateIsCastToItsNaturalType()
    {
        var emitted = Emitted("""routes.Get("/orders/{id:int}", (int id) => id.ToString());""");

        Assert.Contains("Func<int, string>)handler", emitted);
    }

    [Theory]
    [InlineData("Get", "GET")]
    [InlineData("Post", "POST")]
    [InlineData("Put", "PUT")]
    [InlineData("Patch", "PATCH")]
    [InlineData("Delete", "DELETE")]
    public void EveryVerbIsRead(string method, string verb)
    {
        var emitted = Emitted($$"""routes.{{method}}("/orders", () => "ok");""");

        Assert.Contains("\"" + verb + "\"", emitted);
    }

    [Fact]
    public void MapReadsItsVerbFromTheCall()
    {
        var emitted = Emitted("""routes.Map("head", "/orders", () => "ok");""");

        Assert.Contains("\"HEAD\"", emitted);
    }

    /// <remarks>
    /// A type that can be read from a string is a path token, matched by name when the route
    /// registers. The names travel with the handler, because a template that does not declare one
    /// binds nothing.
    /// </remarks>
    [Fact]
    public void AStringConvertibleParameterIsAPathToken()
    {
        var emitted = Emitted(
            """routes.Get("/orders/{id:int}", (int id, Guid trace) => id.ToString());"""
        );

        Assert.Contains("new string[] { \"id\", \"trace\" }", emitted);
    }

    [Fact]
    public void AnInterfaceParameterComesFromTheContainer()
    {
        var emitted = Emitted("""routes.Get("/orders", (IClock clock) => "ok");""");

        Assert.Contains("global::System.Array.Empty<string>()", emitted);
        Assert.Contains("RequestServices", emitted);
    }

    [Fact]
    public void ANonConvertibleParameterIsTheBody()
    {
        var emitted = Emitted("""routes.Post("/orders", (Order body) => body.Id);""");

        Assert.Contains("global::System.Array.Empty<string>()", emitted);
        Assert.DoesNotContain("new string[] { \"body\" }", emitted);
    }

    [Fact]
    public void AnApplicationThatRegistersNoLambdaEmitsNothing()
    {
        var result = Generate("").AssertNoErrors();

        Assert.DoesNotContain(
            result.GeneratedSources.Keys,
            key => key.Contains("RegisteredLambdas", StringComparison.Ordinal)
        );
    }

    // ---- what the build cannot read -----------------------------------------

    private static Diagnostic? Unreadable(string body) =>
        Generate(body)
            .GeneratorDiagnostics.FirstOrDefault(diagnostic =>
                diagnostic.Id
                == Hardened.SourceGenerator.Web.Lambda.LambdaRouteDiagnostics.DiagnosticId
            );

    /// <remarks>
    /// The declared method throws at startup either way. The point of the diagnostic is that the
    /// thing to change is at the call site, which the build can point at and the exception cannot.
    /// </remarks>
    [Fact]
    public void ADelegateHeldInAVariableIsReported()
    {
        var reported = Unreadable(
            """
            Func<int, string> handler = id => id.ToString();

                    routes.Get("/orders/{id:int}", handler);
            """
        );

        Assert.NotNull(reported);
        Assert.Contains("has to be a lambda written here", reported!.GetMessage());
    }

    [Fact]
    public void AVerbFromAVariableIsReported()
    {
        var reported = Unreadable(
            """
            var verb = "GET";

                    routes.Map(verb, "/orders", () => "ok");
            """
        );

        Assert.NotNull(reported);
        Assert.Contains("the verb has to be a constant", reported!.GetMessage());
    }

    [Fact]
    public void AControllerRegistrationIsNotReported()
    {
        Assert.Null(
            Unreadable("""routes.Get("/orders", typeof(Routes), nameof(Routes.Register));""")
        );
    }

    [Fact]
    public void ALambdaWrittenAtTheCallSiteIsNotReported()
    {
        Assert.Null(Unreadable("""routes.Get("/orders/{id:int}", (int id) => id.ToString());"""));
    }
}
