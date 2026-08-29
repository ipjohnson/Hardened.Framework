using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The three generator defects of 2026-08-11, each pinned by name.
///
/// <para>
/// All three emitted C# that did not compile, so any project using the feature failed to build.
/// All three passed every generator test in the suite at the time, because those tests asserted on
/// <c>driver.GetRunResult().Diagnostics</c> — the diagnostics the generator <em>reported</em> — and
/// never compiled what it produced. Integration tests found them, after they shipped.
/// </para>
///
/// <para>
/// Each test here compiles the generated output and then asserts the specific shape that was wrong,
/// so a reintroduction fails on the exact line rather than somewhere downstream.
/// </para>
/// </summary>
public class GeneratedCodeRegressionTests {

    /// <summary>
    /// <c>[FromQueryString("q")]</c>. The name was read with <c>ToFullString()</c>, which returns
    /// the argument's <em>source text</em> — quotes included — and the emitter quoted it a second
    /// time, producing <c>Get(""q"")</c>. Shipped broken; fixed 2026-08-11 by reading the constant
    /// value instead (SyntaxNodeExtensions.GetFirstStringArgumentValue).
    /// </summary>
    [Fact]
    public void ANamedQueryStringBindingEmitsASinglyQuotedName() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/search")]
                public string Search([FromQueryString("q")] string term) => term;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Search");

        Assert.Contains("context.Request.QueryString.Get(\"q\")", source);
        Assert.DoesNotContain("\"\"q\"\"", source);
    }

    /// <summary>
    /// The same defect through the header attribute. <c>[FromHeader("X-Tenant")]</c> is the form the
    /// README documents, and the one every multi-tenant handler uses.
    /// </summary>
    [Fact]
    public void ANamedHeaderBindingEmitsASinglyQuotedName() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/tenant")]
                public string Tenant([FromHeader("X-Tenant")] string tenant) => tenant;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Tenant");

        Assert.Contains("context.Request.Headers.Get(\"X-Tenant\")", source);
        Assert.DoesNotContain("\"\"X-Tenant\"\"", source);
    }

    /// <summary>
    /// A binding name given as a named argument rather than positionally. It reaches the same
    /// re-quoting path, and is the form a constant-driven header name usually takes.
    /// </summary>
    [Fact]
    public void ABindingNameGivenAsAConstantIsResolvedToItsValue() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public static class Headers {
                public const string Tenant = "X-Tenant";
            }

            public class TenantController {
                [Get("/tenant")]
                public string Tenant([FromHeader(Headers.Tenant)] string tenant) => tenant;
            }
            """).AssertNoErrors();

        var source = result.SourceContaining("Tenant");

        Assert.Contains("context.Request.Headers.Get(\"X-Tenant\")", source);
        Assert.DoesNotContain("Headers.Tenant", source);
    }

    /// <summary>
    /// A handler carrying a filter but taking no parameters.
    ///
    /// <para>
    /// <c>ExecutionRequestHandlerInfo</c> takes parameters and metadata as trailing optional
    /// arguments, parameters first. The emitter appended <c>_metadata</c> without filling the
    /// parameters slot, so the metadata array was passed <em>as</em> the parameters array and the
    /// generated code did not compile. Shipped broken; fixed 2026-08-11 by emitting an explicit
    /// <c>null</c> for the parameters slot.
    /// </para>
    /// </summary>
    [Fact]
    public void AHandlerWithMetadataAndNoParametersFillsTheParametersSlot() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class HealthController {
                [Get("/health")]
                [Retry(Retries = 2)]
                public string Health() => "ok";
            }
            """).AssertNoErrors();

        var source = result.SourceContaining("Health");

        Assert.Contains(
            "new global::Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo(\"/health\", \"GET\", typeof(global::TestApp.HealthController), \"Health\", null, _metadata)",
            source);
    }

    /// <summary>
    /// The counterpart: parameters and no metadata. The parameters slot carries <c>_parameterInfo</c>
    /// and nothing is appended after it, so a fix that always emitted a placeholder would show up
    /// here.
    /// </summary>
    [Fact]
    public void AHandlerWithParametersAndNoMetadataPassesOnlyTheParameters() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class OrderController {
                [Get("/orders/{id}")]
                public string GetOrder(string id) => id;
            }
            """).AssertNoErrors();

        var source = result.SourceContaining("GetOrder");

        Assert.Contains(
            "new global::Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo(\"/orders/{id}\", \"GET\", typeof(global::TestApp.OrderController), \"GetOrder\", _parameterInfo)",
            source);
        Assert.DoesNotContain("_metadata", source);
    }

    /// <summary>
    /// Both slots filled, which is the shape the fix has to keep working.
    /// </summary>
    [Fact]
    public void AHandlerWithBothParametersAndMetadataPassesBothInOrder() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class OrderController {
                [Get("/orders/{id}")]
                [Retry(Retries = 2)]
                public string GetOrder(string id) => id;
            }
            """).AssertNoErrors();

        Assert.Contains(
            "\"GetOrder\", _parameterInfo, _metadata)",
            result.SourceContaining("GetOrder"));
    }

    /// <summary>
    /// Neither slot filled. A zero-parameter, unfiltered handler is the smallest handler there is,
    /// and the only shape where the trailing arguments are both absent.
    /// </summary>
    [Fact]
    public void AHandlerWithNeitherParametersNorMetadataPassesNeither() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class HealthController {
                [Get("/health")]
                public string Health() => "ok";
            }
            """).AssertNoErrors();

        Assert.Contains(
            "new global::Hardened.Requests.Runtime.Execution.ExecutionRequestHandlerInfo(\"/health\", \"GET\", typeof(global::TestApp.HealthController), \"Health\")",
            result.SourceContaining("Health"));
    }

    /// <summary>
    /// <c>[FromHeader]</c> binding.
    ///
    /// <para>
    /// Generated binding code calls <c>Get</c> uniformly on <c>PathTokens</c>, <c>QueryString</c>
    /// and <c>Headers</c>, but <c>IExecutionRequest.Headers</c> is a plain
    /// <c>IDictionary&lt;string, StringValues&gt;</c>, which has no <c>Get</c>. Every
    /// <c>[FromHeader]</c> handler failed to compile — a feature documented from the framework's
    /// first release. Fixed 2026-08-11 by adding
    /// <c>Hardened.Requests.Runtime.Execution.HeaderDictionaryExtensions.Get</c>, in a namespace
    /// generated handlers already import.
    /// </para>
    /// </summary>
    [Fact]
    public void HeaderBindingResolvesGetOnThePlainHeaderDictionary() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/tenant")]
                public string Tenant([FromHeader("X-Tenant")] string tenant) => tenant;
            """)).AssertNoErrors();

        var source = result.SourceContaining("Tenant");

        Assert.Contains("context.Request.Headers.Get(\"X-Tenant\")", source);
        Assert.Contains("using Hardened.Requests.Runtime.Execution;", source);
    }

    /// <summary>
    /// An unnamed <c>[FromHeader]</c> falls back to the parameter name. Same call, no argument to
    /// re-quote, so it stayed broken for the same reason and is fixed by the same extension.
    /// </summary>
    [Fact]
    public void AnUnnamedHeaderBindingUsesTheParameterName() {
        var result = RequestGeneratorHarness.Generate(RequestGeneratorHarness.Controller("""
                [Get("/tenant")]
                public string Tenant([FromHeader] string tenant) => tenant;
            """)).AssertNoErrors();

        Assert.Contains("context.Request.Headers.Get(\"tenant\")", result.SourceContaining("Tenant"));
    }

    /// <summary>
    /// All three defect shapes in one handler. Each was found separately; nothing had ever compiled
    /// them together, and the parameters slot is exactly where a combination would go wrong.
    /// </summary>
    [Fact]
    public void AllThreeRegressionShapesCompileTogether() {
        var result = RequestGeneratorHarness.Generate("""
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class TenantController {
                [Get("/tenant/search")]
                [Retry(Retries = 2)]
                public string Search(
                    [FromQueryString("q")] string term,
                    [FromHeader("X-Tenant")] string tenant) => term + tenant;

                [Get("/tenant/health")]
                [Retry(Retries = 2)]
                public string Health() => "ok";
            }
            """).AssertNoErrors();

        Assert.Contains("_parameterInfo, _metadata)", result.SourceContaining("Search"));
        Assert.Contains("null, _metadata)", result.SourceContaining("Health"));
    }
}
