using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// Which attributes BaseRequestModelGenerator treats as filters, and what it does with them.
///
/// <para>
/// The rule is subtractive: everything on a handler or its controller is a filter <em>except</em>
/// the route verbs and the two response-shaping attributes. Getting it wrong in either direction is
/// silent — an attribute wrongly excluded means a filter that never runs, and one wrongly included
/// means <c>[Get("/x")]</c> constructed into the metadata array, which does not compile.
/// </para>
/// </summary>
public class FilterAttributeDetectionTests {

    private const string Attributes = """
        using System;
        using Hardened.Requests.Runtime.Filters;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        public class AuditAttribute : Attribute {
            public AuditAttribute(string name) { Name = name; }

            public string Name { get; }

            public int Level { get; set; }
        }

        public class TraceAttribute : Attribute { }

        public class ThrottleAttribute : Attribute { }
        """;

    private static string WithAttributes(string controller) => Attributes + Environment.NewLine + controller;

    [Fact]
    public void AMethodAttributeBecomesHandlerMetadata() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Trace]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains("new global::TestApp.TraceAttribute()", result.SourceContaining("All"));
    }

    [Fact]
    public void AControllerAttributeBecomesHandlerMetadata() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            [Trace]
            public class OrderController {
                [Get("/orders")]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains("new global::TestApp.TraceAttribute()", result.SourceContaining("All"));
    }

    /// <summary>
    /// Method filters precede controller filters. Filter order decides execution order, so the
    /// handler's own declaration winning over the controller's is the observable behaviour rather
    /// than an implementation detail.
    /// </summary>
    [Fact]
    public void MethodFiltersPrecedeControllerFilters() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            [Trace]
            public class OrderController {
                [Get("/orders")]
                [Throttle]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        var source = result.SourceContaining("All");

        Assert.Contains(
            "new object[] { new global::TestApp.ThrottleAttribute(), new global::TestApp.TraceAttribute() }",
            source);
    }

    /// <summary>
    /// Several filters on one method keep their declaration order, which is the order a reader of
    /// the controller expects them to run in.
    /// </summary>
    [Fact]
    public void MultipleMethodFiltersKeepTheirDeclarationOrder() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Trace]
                [Throttle]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains(
            "new object[] { new global::TestApp.TraceAttribute(), new global::TestApp.ThrottleAttribute() }",
            result.SourceContaining("All"));
    }

    /// <summary>Filters declared in one bracketed list, rather than one list each.</summary>
    [Fact]
    public void FiltersInASingleAttributeListAreAllDetected() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Trace, Throttle]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains(
            "new object[] { new global::TestApp.TraceAttribute(), new global::TestApp.ThrottleAttribute() }",
            result.SourceContaining("All"));
    }

    [Fact]
    public void AFilterKeepsItsConstructorArguments() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Audit("orders")]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains("new global::TestApp.AuditAttribute(\"orders\")", result.SourceContaining("All"));
    }

    /// <summary>
    /// Named arguments become an object initialiser rather than a constructor argument. The two go
    /// down different paths in AttributeModelHelper, and an attribute using both is the case where
    /// mixing them up produces a call to a constructor that does not exist.
    /// </summary>
    [Fact]
    public void AFilterSplitsPositionalArgumentsFromPropertyAssignments() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Audit("orders", Level = 2)]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains("new global::TestApp.AuditAttribute(\"orders\"){ Level = 2 }",
            result.SourceContaining("All"));
    }

    /// <summary>
    /// <c>[Retry]</c> is a shipped filter that configures itself entirely by property. It reaches
    /// the metadata array with no constructor arguments and an initialiser.
    /// </summary>
    [Fact]
    public void AShippedFilterConfiguredOnlyByPropertyReachesTheMetadata() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Retry(Retries = 3)]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.Contains(
            "new global::Hardened.Requests.Runtime.Filters.RetryAttribute(){ Retries = 3 }",
            result.SourceContaining("All"));
    }

    /// <summary>
    /// The route attribute itself is never a filter. It is the one attribute guaranteed to be
    /// present on every handler, so including it would break every generated file at once.
    /// </summary>
    [Theory]
    [InlineData("Get")]
    [InlineData("Post")]
    [InlineData("Put")]
    [InlineData("Delete")]
    [InlineData("Patch")]
    public void TheRouteVerbIsNotAFilter(string verb) {
        var result = RequestGeneratorHarness.Generate(WithAttributes($$"""
            public class OrderController {
                [{{verb}}("/orders")]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        var source = result.SourceContaining("All");

        Assert.DoesNotContain("_metadata", source);
        Assert.DoesNotContain($"{verb}Attribute(", source);
    }

    /// <summary>
    /// The verb written in its full <c>Attribute</c> form. The exclusion list holds both spellings,
    /// and only the short one is ever exercised by hand-written controllers.
    /// </summary>
    [Fact]
    public void TheRouteVerbWrittenInFullIsStillNotAFilter() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [GetAttribute("/orders")]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        Assert.DoesNotContain("_metadata", result.SourceContaining("All"));
    }

    /// <summary>
    /// A handler carrying no filters at all emits no metadata field, and passes neither slot to
    /// ExecutionRequestHandlerInfo.
    /// </summary>
    [Fact]
    public void AHandlerWithNoFiltersEmitsNoMetadata() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        var source = result.SourceContaining("All");

        Assert.DoesNotContain("_metadata", source);
        Assert.Contains("GetFilterInfo()", source);
    }

    /// <summary>
    /// A filtered handler passes its metadata to GetFilterInfo as well as to the handler info. The
    /// metadata field is emitted before _handlerInfo so static initialisation order holds; a filter
    /// that arrived as null at run time would be the symptom of getting that wrong.
    /// </summary>
    [Fact]
    public void AFilteredHandlerPassesItsMetadataToTheFilterLookup() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            public class OrderController {
                [Get("/orders")]
                [Trace]
                public string All() => "x";
            }
            """)).AssertNoErrors();

        var source = result.SourceContaining("All");

        Assert.Contains("GetFilterInfo(_metadata)", source);
        Assert.True(
            source.IndexOf("_metadata =", StringComparison.Ordinal) <
            source.IndexOf("_handlerInfo =", StringComparison.Ordinal),
            "_metadata must be declared before _handlerInfo, or the static initialiser that reads " +
            "it runs first and the handler is built with no filters");
    }

    /// <summary>
    /// Filters on both a parameterised handler and the controller. Metadata and parameters are
    /// independent, and this is the combination where the parameters slot has to hold both.
    /// </summary>
    [Fact]
    public void FiltersAndParametersCoexistOnOneHandler() {
        var result = RequestGeneratorHarness.Generate(WithAttributes("""
            [Trace]
            public class OrderController {
                [Get("/orders/{id}")]
                [Audit("order")]
                public string One(string id) => id;
            }
            """)).AssertNoErrors();

        Assert.Contains("\"One\", _parameterInfo, _metadata)", result.SourceContaining("One"));
    }
}
