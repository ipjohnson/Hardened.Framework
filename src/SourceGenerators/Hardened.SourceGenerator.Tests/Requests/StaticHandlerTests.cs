using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Handlers declared <c>static</c>, which a code-first application can write and a described one
/// cannot.
///
/// <para>
/// The pipeline is built around a controller instance the container constructs, so a static handler
/// has to reach it without one: the invocation calls the declaring type, the controller type
/// argument becomes <c>object</c>, and the runtime reads that as the signal to skip construction.
/// Every test here ends in AssertNoErrors because the whole point is what compiles - the shape
/// these produced before was five compiler errors in a file nobody wrote.
/// </para>
/// </summary>
public class StaticHandlerTests
{
    private static string Application(string controllers) =>
        $$"""
            using System;
            using System.Collections.Generic;
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Shared.Runtime.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class Application { }

            {{controllers}}
            """;

    [Fact]
    public void AStaticHandlerIsCalledOnItsDeclaringType()
    {
        var result = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public static string List() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors();

        var source = result.SourceContaining("List");

        Assert.Contains(
            "context.Response.ResponseValue = global::TestApp.OrderController.List();",
            source
        );
        Assert.DoesNotContain("controller.List()", source);
    }

    /// <summary>
    /// The case that cannot be reached any other way: a static class can be a controller, and it
    /// can be a type argument to nothing.
    /// </summary>
    [Fact]
    public void AStaticClassCanBeAController()
    {
        var result = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public static class OrderController {
                        [Get("/orders")]
                        public static string List() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors();

        var source = result.SourceContaining("List");

        Assert.Contains("BaseExecutionHandler<object>", source);
        Assert.Contains("global::TestApp.OrderController.List()", source);
    }

    [Fact]
    public void TheControllerTypeArgumentBecomesObject()
    {
        var source = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public static string List() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("List");

        Assert.Contains("StandardFilterEmptyParameters<object>", source);
        Assert.Contains("IExecutionContext context, object controller)", source);
    }

    /// <summary>
    /// The substitution is the type argument and nothing else. A convention, a global filter and
    /// the published document all read the handler info, and a static handler has to look like any
    /// other there.
    /// </summary>
    [Fact]
    public void TheHandlerInfoStillNamesTheDeclaringType()
    {
        var source = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public static class OrderController {
                        [Get("/orders")]
                        public static string List() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("List");

        Assert.Contains("typeof(global::TestApp.OrderController)", source);
    }

    [Fact]
    public void AStaticHandlerBindsItsParameters()
    {
        var result = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public static class OrderController {
                        [Get("/orders/{id}")]
                        public static string Get(string id) => id;
                    }
                    """
                )
            )
            .AssertNoErrors();

        var source = result.SourceContaining("Get");

        Assert.Contains("global::TestApp.OrderController.Get(parameters.id)", source);
    }

    [Fact]
    public void AnAsyncStaticHandlerIsAwaited()
    {
        var result = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public static class OrderController {
                        [Get("/orders")]
                        public static Task<string> List() => Task.FromResult("x");
                    }
                    """
                )
            )
            .AssertNoErrors();

        Assert.Contains(
            "context.Response.ResponseValue = await global::TestApp.OrderController.List();",
            result.SourceContaining("List")
        );
    }

    /// <summary>
    /// A static handler's declaring type is resolved by nobody, and where it is a static class it
    /// cannot be registered at all.
    /// </summary>
    [Fact]
    public void AnAllStaticControllerIsNotRegistered()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public static class OrderController {
                        [Get("/orders")]
                        public static string List() => "x";
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.DoesNotContain("AddTransient<OrderController>", routing);
    }

    /// <summary>
    /// And a controller holding both kinds keeps its registration, because its instance handler
    /// still needs one.
    /// </summary>
    [Fact]
    public void AControllerWithBothKindsIsStillRegistered()
    {
        var routing = RequestGeneratorHarness
            .Generate(
                Application(
                    """
                    public class OrderController {
                        [Get("/orders")]
                        public static string List() => "x";

                        [Get("/orders/{id}")]
                        public string Get(string id) => id;
                    }
                    """
                )
            )
            .AssertNoErrors()
            .SourceContaining("Routing");

        Assert.Contains("AddTransient<OrderController>", routing);
    }
}
