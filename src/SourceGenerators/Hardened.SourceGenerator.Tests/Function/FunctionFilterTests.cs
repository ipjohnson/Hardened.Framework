using Hardened.SourceGenerator.Tests.Infrastructure;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Function;

/// <summary>
/// Which attributes on a function handler are read as filters, and which are read as something
/// else.
///
/// <para>
/// <c>FunctionModelGenerator.IsFilterAttribute</c> makes this call, and it is a denylist: everything
/// is a filter except <c>[Template]</c>, <c>[RawResponse]</c> and <c>[HardenedFunction]</c> itself.
/// Getting it wrong in either direction is quiet — a filter mistaken for response information never
/// runs, and <c>[HardenedFunction]</c> mistaken for a filter is instantiated as one at startup.
/// </para>
/// </summary>
public class FunctionFilterTests {

    private static string Handler(string attributes, string signature) =>
        FunctionGeneratorHarness.Generate($$"""
            using System.Threading.Tasks;
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            public class TestFunctions {
                [HardenedFunction]
                {{attributes}}
                {{signature}}
            }
            """).AssertNoErrors().SourceContaining("Process.FunctionHandler");

    /// <summary>
    /// A filter attribute reaches the handler's metadata array, and the array is passed to
    /// <c>GetFilterInfo</c> — an attribute recorded but not passed would never run.
    /// </summary>
    [Fact]
    public void AFilterAttributeIsRecordedInTheHandlerMetadata() {
        var source = Handler("[Retry(Retries = 2)]", "public void Process() { }");

        Assert.Contains("new global::Hardened.Requests.Runtime.Filters.RetryAttribute(){ Retries = 2 }", source);
        Assert.Contains("ExecutionHelper.GetFilterInfo(_metadata)", source);
    }

    /// <summary>
    /// <c>[HardenedFunction]</c> is what marks the handler, not a filter on it. Treated as one it
    /// would be constructed and run on every invocation.
    /// </summary>
    [Fact]
    public void TheHardenedFunctionAttributeIsNotItselfAFilter() {
        var source = Handler("", "public void Process() { }");

        Assert.DoesNotContain("_metadata", source);
        Assert.DoesNotContain("HardenedFunctionAttribute()", source);
    }

    /// <summary>
    /// <c>[RawResponse]</c> is response information: it becomes the handler's output function
    /// rather than a filter, so it stays out of the metadata array.
    /// </summary>
    [Fact]
    public void ARawResponseAttributeBecomesTheOutputFunctionRatherThanAFilter() {
        var source = Handler("[RawResponse(\"text/csv\")]", "public string Process() => \"a,b\";");

        Assert.Contains("RawOutputHelper.OutputFunc(\"text/csv\")", source);
        Assert.DoesNotContain("_metadata", source);
    }


    /// <summary>
    /// <c>[Template]</c> is read the same way: response information, not a filter, so it stays
    /// out of the metadata array.
    ///
    /// It no longer emits an output function. The template engine it called into was removed, and
    /// the attribute is kept as the annotation a future renderer will bind to — so what is
    /// asserted here is the classification, which is unchanged, and not an emission that no
    /// longer happens.
    /// </summary>
    [Fact]
    public void ATemplateAttributeIsResponseInformationRatherThanAFilter() {
        var source = Handler("[Template(\"Index\")]", "public string Process() => \"x\";");

        Assert.DoesNotContain("_metadata", source);
    }

    /// <summary>
    /// A filter beside response information. The two are decided independently, so this is the
    /// case where one being mistaken for the other shows up.
    /// </summary>
    [Fact]
    public void AFilterAndAResponseAttributeCoexistOnOneHandler() {
        var source = Handler(
            """
            [Retry(Retries = 3)]
            [RawResponse("text/csv")]
            """,
            "public string Process() => \"a,b\";");

        Assert.Contains("RetryAttribute(){ Retries = 3 }", source);
        Assert.Contains("RawOutputHelper.OutputFunc(\"text/csv\")", source);
    }

    /// <summary>
    /// A filter declared on the handler's class reaches every function it declares — filters are
    /// collected from the class as well as the method.
    /// </summary>
    [Fact]
    public void AFilterOnTheClassReachesItsFunctions() {
        var source = FunctionGeneratorHarness.Generate("""
            using Hardened.Requests.Abstract.Attributes;
            using Hardened.Requests.Runtime.Filters;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class TestApplication { }

            [Retry(Retries = 4)]
            public class TestFunctions {
                [HardenedFunction]
                public void Process() { }
            }
            """).AssertNoErrors().SourceContaining("Process.FunctionHandler");

        Assert.Contains("RetryAttribute(){ Retries = 4 }", source);
    }
}
