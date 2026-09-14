using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Hardened.SourceGenerator.Web.Lambda;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Web;

/// <summary>
/// A route registered with a lambda, read through this project's own generator.
/// </summary>
/// <remarks>
/// <c>Hardened.Web.SourceGenerator.Tests</c> drives the same code through the web generator and
/// compiles what it emits, which is the stronger assertion. Every generator here compiles
/// <c>Hardened.SourceGenerator</c>'s sources in rather than referencing the assembly, so that run
/// says nothing about this one - and coverage is measured per assembly.
/// </remarks>
public class LambdaRegistrationTests
{
    private static string Application(string body) =>
        $$"""
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Requests.Abstract.Execution;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;
            using Hardened.Web.Runtime.Routing;

            namespace TestApp;

            [HardenedModule]
            public partial class Application { }

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
        RequestGeneratorHarness.Generate(Application(body));

    private static string Emitted(string body) =>
        Generate(body).AssertNoErrors().SourceContaining("RegisteredLambdas");

    [Fact]
    public void ALambdaIsReadFromItsCallSite()
    {
        var emitted = Emitted("""routes.Get("/orders/{id:int}", (int id) => id.ToString());""");

        Assert.Contains("InterceptsLocation", emitted);
        Assert.Contains("\"GET\"", emitted);
        Assert.Contains("new string[] { \"id:int|range\" }", emitted);
    }

    /// <remarks>
    /// Every special type a handler method can take, taken by a lambda on the same terms - and none
    /// of them a path token.
    /// </remarks>
    [Theory]
    [InlineData("IExecutionContext context")]
    [InlineData("IExecutionRequest request")]
    [InlineData("IExecutionResponse response")]
    [InlineData("IServiceProvider services")]
    [InlineData("CancellationToken token")]
    [InlineData("IClock clock")]
    [InlineData("Order body")]
    public void AParameterThatIsNotAPathTokenBindsNoToken(string parameter)
    {
        var emitted = Emitted($$"""routes.Post("/orders", ({{parameter}}) => "ok");""");

        Assert.Contains("global::System.Array.Empty<string>()", emitted);
    }

    [Theory]
    [InlineData("int id")]
    [InlineData("string id")]
    [InlineData("Guid id")]
    [InlineData("long id")]
    [InlineData("bool id")]
    [InlineData("decimal id")]
    [InlineData("DateTime id")]
    [InlineData("DateOnly id")]
    [InlineData("TimeSpan id")]
    [InlineData("Uri id")]
    [InlineData("int? id")]
    public void AStringConvertibleParameterIsAPathToken(string parameter)
    {
        var emitted = Emitted($$"""routes.Get("/orders/{id}", ({{parameter}}) => "ok");""");

        Assert.Contains("new string[] { \"id", emitted);
    }

    /// <summary>
    /// And it names the constraints that would keep a value it cannot read away from its binder.
    /// </summary>
    /// <remarks>
    /// The registry holds the registration to one of them. A registered route's operation is
    /// written before its path exists, so whether a bad value answers 404 or 400 has to be settled
    /// at the call site rather than read off a template the build never sees.
    /// </remarks>
    [Theory]
    [InlineData("int id", "id:int|range")]
    [InlineData("long id", "id:long|int|min|max|range")]
    [InlineData("Guid id", "id:guid")]
    [InlineData("bool id", "id:bool")]
    [InlineData("DateOnly id", "id:date")]
    [InlineData("DateTime id", "id:datetime|date")]
    // A string binds as itself and its converter can refuse nothing, so it is held to nothing.
    [InlineData("string id", "id")]
    // And a type no constraint tests keeps the 400 its converter can still answer.
    [InlineData("TimeSpan id", "id")]
    [InlineData("Uri id", "id")]
    public void APathTokenNamesTheConstraintsThatSatisfyIt(string parameter, string expected)
    {
        var emitted = Emitted($$"""routes.Get("/orders/{id}", ({{parameter}}) => "ok");""");

        Assert.Contains($"new string[] {{ \"{expected}\" }}", emitted);
    }

    /// <remarks>
    /// The converter does read one from base64, but a <c>byte[]</c> parameter is the request body
    /// and has been since raw bodies landed. Reading it as a token would take the body away from
    /// the handler that declared it.
    /// </remarks>
    [Fact]
    public void AByteArrayIsTheBodyRatherThanAToken()
    {
        var emitted = Emitted("""routes.Post("/blobs", (byte[] body) => "ok");""");

        Assert.Contains("global::System.Array.Empty<string>()", emitted);
    }

    [Fact]
    public void AVoidLambdaIsAnAction()
    {
        var emitted = Emitted("""routes.Post("/orders", () => { });""");

        Assert.Contains("global::System.Action", emitted);
    }

    [Fact]
    public void AnAsyncLambdaIsAwaited()
    {
        var emitted = Emitted(
            """routes.Get("/orders/{id:int}", (int id) => Task.FromResult(id));"""
        );

        Assert.Contains("await", emitted);
    }

    // ---- what the build cannot read -----------------------------------------

    private static Diagnostic? Unreadable(string body) =>
        Generate(body)
            .GeneratorDiagnostics.FirstOrDefault(diagnostic =>
                diagnostic.Id == LambdaRouteDiagnostics.DiagnosticId
            );

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
    public void AControllerRegistrationIsNotReported() =>
        Assert.Null(
            Unreadable("""routes.Get("/orders", typeof(Routes), nameof(Routes.Register));""")
        );

    [Fact]
    public void ACallOnSomethingElseEntirelyIsNotReported() =>
        Assert.Null(Unreadable("""var text = "a,b".Split(',').ToString();"""));
}
