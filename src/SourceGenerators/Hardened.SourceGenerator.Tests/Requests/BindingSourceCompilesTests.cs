using Hardened.SourceGeneration.Testing;
using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Every binding source the request generator supports, each alone and then combined, compiled
/// rather than string-matched.
///
/// <para>
/// The distinction matters: until 2026-08-11 the generator suites asserted only on
/// <c>driver.GetRunResult().Diagnostics</c> — what the generator <em>reported</em> — so three
/// separate defects that emitted uncompilable C# passed every test and shipped. Each case here ends
/// in <see cref="GeneratorResult.AssertNoErrors"/>, which compiles the input together with the
/// generated trees. See docs/testing-conventions.md §1.
/// </para>
/// </summary>
public class BindingSourceCompilesTests {

    [Fact]
    public void PathTokenBinds() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;
            """)).AssertNoErrors();

        Assert.Contains("context.Request.PathTokens.Get(\"id\")", result.SourceContaining("GetOrder"));
    }

    /// <summary>
    /// A parameter that is neither a path token, nor attributed, nor an interface falls through to
    /// body binding. It is the default, so it is the case a regression is least likely to name.
    /// </summary>
    [Fact]
    public void AnUnattributedComplexParameterBindsFromTheBody() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record OrderModel(string Sku);

            public class OrderController {
                [Post("/orders")]
                public string Create(OrderModel model) => model.Sku;
            }
            """).AssertNoErrors();

        Assert.Contains("DeserializeRequestBody", result.SourceContaining("Create"));
    }

    [Fact]
    public void ExplicitFromBodyBinds() {
        RequestGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record OrderModel(string Sku);

            public class OrderController {
                [Post("/orders")]
                public string Create([FromBody] OrderModel model) => model.Sku;
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void QueryStringBindsUnderTheParameterName() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/search")]
                public string Search([FromQueryString] string term) => term;
            """)).AssertNoErrors();

        Assert.Contains("context.Request.QueryString.Get(\"term\")", result.SourceContaining("Search"));
    }

    [Fact]
    public void HeaderBindsUnderTheHeaderName() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/tenant")]
                public string Tenant([FromHeader("X-Tenant")] string tenant) => tenant;
            """)).AssertNoErrors();

        Assert.Contains("context.Request.Headers.Get(\"X-Tenant\")", result.SourceContaining("Tenant"));
    }

    /// <summary>
    /// An interface parameter with no attribute resolves from the request's service provider. That
    /// is what makes <c>[FromServices]</c> optional in practice, and what a controller taking a
    /// domain service relies on.
    /// </summary>
    [Fact]
    public void AnInterfaceParameterResolvesFromTheServiceProvider() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public interface IClock { string Now(); }

            public class TimeController {
                [Get("/time")]
                public string Now(IClock clock) => clock.Now();
            }
            """).AssertNoErrors();

        Assert.Contains("GetRequiredService", result.SourceContaining("Now"));
    }

    [Fact]
    public void ExplicitFromServicesResolvesFromTheServiceProvider() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public interface IClock { string Now(); }

            public class TimeController {
                [Get("/time")]
                public string Now([FromServices] IClock clock) => clock.Now();
            }
            """).AssertNoErrors();

        Assert.Contains("GetRequiredService", result.SourceContaining("Now"));
    }

    /// <summary>
    /// A closed generic service. The generated code names the type in full, so an open/closed
    /// mix-up shows up as a compile error rather than a resolution failure at run time.
    /// </summary>
    [Fact]
    public void AGenericServiceParameterResolvesFromTheServiceProvider() {
        RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public interface IMathService<T> { T Add(T left, T right); }

            public class MathController {
                [Get("/math")]
                public int Add(IMathService<int> mathService) => mathService.Add(1, 2);
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void NestedGenericServiceParametersResolve() {
        RequestGeneratorHarness.Generate("""
            using System.Collections.Generic;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public interface IRepository<T> { IReadOnlyList<T> All(); }

            public class ListController {
                [Get("/lists")]
                public int Count(IRepository<IReadOnlyList<string>> repository) => repository.All().Count;
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void TheServiceProviderItselfBinds() {
        var result = RequestGeneratorHarness.Generate("""
            using System;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class RootController {
                [Get("/root")]
                public string Root(IServiceProvider serviceProvider) => serviceProvider.ToString()!;
            }
            """).AssertNoErrors();

        Assert.Contains("context.RequestServices", result.SourceContaining("Root"));
    }

    [Theory]
    [InlineData("IExecutionContext", "context")]
    [InlineData("IExecutionRequest", "context.Request")]
    [InlineData("IExecutionResponse", "context.Response")]
    public void ExecutionPipelineTypesBindDirectly(string parameterType, string expectedSource) {
        var result = RequestGeneratorHarness.Generate($$"""
            using Hardened.Requests.Abstract.Execution;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class PipelineController {
                [Get("/pipeline")]
                public string Pipeline({{parameterType}} pipeline) => pipeline.ToString()!;
            }
            """).AssertNoErrors();

        Assert.Contains($"parameters.pipeline = {expectedSource};", result.SourceContaining("Pipeline"));
    }

    /// <summary>
    /// An attribute the generator does not recognise is treated as a custom binding source: the
    /// attribute is constructed in the generated binder and handed to
    /// <c>ExecutionHelper.CustomAttributeData</c>. Its constructor arguments are re-emitted, which
    /// is the mechanism that produced the double-quoted-literal defect — see
    /// <see cref="GeneratedCodeRegressionTests"/>.
    /// </summary>
    [Fact]
    public void ACustomBindingAttributeConstructsTheAttributeInTheBinder() {
        var result = RequestGeneratorHarness.Generate("""
            using System;
            using Hardened.Requests.Abstract.Execution;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [AttributeUsage(AttributeTargets.Parameter)]
            public class FromClaimAttribute : Attribute {
                public FromClaimAttribute(string claim) { Claim = claim; }

                public string Claim { get; }
            }

            public class ClaimController {
                [Get("/claim")]
                public string Claim([FromClaim("sub")] string subject) => subject;
            }
            """).AssertNoErrors();

        var source = result.SourceContaining("Claim");

        Assert.Contains("CustomAttributeData", source);
        Assert.Contains("new global::TestApp.FromClaimAttribute(\"sub\")", source);
    }

    /// <summary>
    /// A custom binding attribute configured by property rather than by constructor argument.
    ///
    /// <para>
    /// Only the compilation is asserted. The property assignment is currently dropped — the binder
    /// emits <c>new FromClaimAttribute()</c> — because BindFromCustomAttribute re-emits
    /// <c>AttributeModel.Arguments</c> and never <c>AttributeModel.PropertyAssignment</c>, where
    /// HandlerInfoCodeGenerator.CreateMetadataField does emit both for filters. That is reported as
    /// a defect rather than asserted here: a test that pinned the current output would make the fix
    /// look like the regression. See docs/testing-conventions.md §6.
    /// </para>
    /// </summary>
    [Fact]
    public void APropertyConfiguredCustomBindingAttributeCompiles() {
        RequestGeneratorHarness.Generate("""
            using System;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            [AttributeUsage(AttributeTargets.Parameter)]
            public class FromClaimAttribute : Attribute {
                public string Claim { get; set; } = "";
            }

            public class ClaimController {
                [Get("/claim")]
                public string Claim([FromClaim(Claim = "sub")] string subject) => subject;
            }
            """).AssertNoErrors();
    }

    /// <summary>
    /// A required value goes through ParseRequired, which reports a missing value as a 400 rather
    /// than binding null into a non-nullable parameter.
    /// </summary>
    [Fact]
    public void ANonNullableValueBindsAsRequired() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/search")]
                public string Search([FromQueryString] string term) => term;
            """)).AssertNoErrors();

        Assert.Contains("ParseRequired", result.SourceContaining("Search"));
    }

    [Fact]
    public void ANullableValueBindsAsOptional() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/search")]
                public string? Search([FromQueryString] string? term) => term;
            """)).AssertNoErrors();

        Assert.Contains("ParseOptional", result.SourceContaining("Search"));
    }

    [Fact]
    public void AParameterWithADefaultBindsWithThatDefault() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/search")]
                public int Page([FromQueryString] int page = 3) => page;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Page");

        Assert.Contains("ParseWithDefault", source);
        Assert.Contains("3", source);
    }

    /// <summary>
    /// Conversion is the string converter service's job, but the generated code names the target
    /// type in the generic argument, so every type has to survive being written out.
    /// </summary>
    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("bool")]
    [InlineData("decimal")]
    [InlineData("System.Guid")]
    [InlineData("System.DateTime")]
    [InlineData("System.DateTimeOffset")]
    [InlineData("System.TimeSpan")]
    public void EveryConvertibleValueTypeBindsFromAPathToken(string parameterType) {
        RequestGeneratorHarness.Generate($$"""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ValueController {
                [Get("/value/{token}")]
                public string Value({{parameterType}} token) => token.ToString()!;
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void AnEnumPathTokenBinds() {
        RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public enum OrderState { Open, Closed }

            public class StateController {
                [Get("/state/{state}")]
                public string State(OrderState state) => state.ToString();
            }
            """).AssertNoErrors();
    }

    [Fact]
    public void MultiplePathTokensBindInOrder() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/pair/{first}/{second}")]
                public string Pair(string first, string second) => first + second;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Pair");

        Assert.Contains("context.Request.PathTokens.Get(\"first\")", source);
        Assert.Contains("context.Request.PathTokens.Get(\"second\")", source);
    }

    /// <summary>
    /// The conjunction is the behaviour: parameters are addressed by index in the generated
    /// Parameters class and in _parameterInfo, so mixing sources is where an off-by-one shows up.
    /// </summary>
    [Fact]
    public void AllBindingSourcesCombineInASingleHandler() {
        var result = RequestGeneratorHarness.Generate("""
            using System;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Requests.Abstract.Execution;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record OrderModel(string Sku);

            public interface IMathService<T> { T Add(T left, T right); }

            [AttributeUsage(AttributeTargets.Parameter)]
            public class FromClaimAttribute : Attribute {
                public FromClaimAttribute(string claim) { Claim = claim; }

                public string Claim { get; }
            }

            public class MixedController {
                [Post("/mixed/{id}")]
                public string Mixed(
                    string id,
                    [FromQueryString] string filter,
                    [FromHeader("X-Tenant")] string tenant,
                    [FromBody] OrderModel model,
                    [FromClaim("sub")] string subject,
                    IMathService<int> mathService,
                    IServiceProvider serviceProvider,
                    IExecutionContext context) =>
                    id + filter + tenant + model.Sku + subject + mathService.Add(1, 2);
            }
            """).AssertNoErrors();

        var source = result.SourceContaining("Mixed");

        // Every parameter keeps its own slot: index n in _parameterInfo is parameter n.
        Assert.Contains("new global::Hardened.Requests.Abstract.Execution.IExecutionRequestParameter[8]", source);
        Assert.Contains("_parameterInfo[4]", source);

        // ParameterCount is no longer emitted - it comes from ExecutionRequestParameters as
        // Info.Count, which is this same array. Asserting the base type is what pins that the
        // count, the by-name lookup and Clone are all still reachable.
        Assert.Contains("ExecutionRequestParameters", source);
    }

    /// <summary>
    /// Two sources that both need <c>await</c> — body deserialisation and a custom attribute — in
    /// one binder. The binder is emitted async only when something in it awaits, so a handler
    /// combining both is the case where that decision could be made twice.
    /// </summary>
    [Fact]
    public void BodyAndCustomAttributeBindingShareOneAsyncBinder() {
        var result = RequestGeneratorHarness.Generate("""
            using System;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public record OrderModel(string Sku);

            [AttributeUsage(AttributeTargets.Parameter)]
            public class FromClaimAttribute : Attribute {
                public FromClaimAttribute(string claim) { Claim = claim; }

                public string Claim { get; }
            }

            public class MixedController {
                [Post("/mixed")]
                public string Mixed([FromBody] OrderModel model, [FromClaim("sub")] string subject) =>
                    model.Sku + subject;
            }
            """).AssertNoErrors();

        var source = result.SourceContaining("Mixed");

        Assert.Contains("async global::System.Threading.Tasks.Task<", source);
        Assert.DoesNotContain("Task.FromResult", source);
    }

    /// <summary>
    /// The mirror image: nothing in the binder awaits, so it returns a completed task rather than
    /// paying for a state machine on every request.
    /// </summary>
    [Fact]
    public void ABinderWithNothingToAwaitReturnsACompletedTask() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;
            """)).AssertNoErrors();

        var source = result.SourceContaining("GetOrder");

        Assert.Contains("Task.FromResult", source);
        Assert.DoesNotContain("async global::System.Threading.Tasks.Task<", source);
    }

    /// <summary>
    /// Two handlers on the same controller with the same name and different parameters. The invoke
    /// class name is derived from the parameter names, so a collision here would emit the same hint
    /// name twice and silently drop one handler — which AssertNoErrors also checks for.
    /// </summary>
    [Fact]
    public void OverloadedHandlersGetDistinctInvokeClasses() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/one/{id}")]
                public string Handle(string id) => id;

                [Get("/two/{other}")]
                public string Handle(string other, [FromQueryString] string filter) => other + filter;
            """)).AssertNoErrors();

        Assert.Equal(2, result.GeneratedSources.Count);
    }

    /// <summary>A source with nothing to generate from is not an error.</summary>
    [Fact]
    public void AFileWithNoHandlersGeneratesNothing() {
        var result = RequestGeneratorHarness.Generate("""
            namespace TestApp;

            public class NotAController {
                public string Value => "x";
            }
            """).AssertNoErrors();

        Assert.Empty(result.GeneratedSources);
    }
}
