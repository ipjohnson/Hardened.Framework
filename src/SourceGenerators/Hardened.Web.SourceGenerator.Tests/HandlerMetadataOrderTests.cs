using System.Text.RegularExpressions;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Filters;
using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// A handler's own attributes are emitted into its metadata ahead of its class's.
/// </summary>
/// <remarks>
/// <para>
/// This was incidental until the timeout cascade started depending on it.
/// <c>IExecutionRequestHandlerInfo.TimeoutFrom</c> takes the <em>first</em> declaration in metadata
/// rather than the tightest, because first-match is what makes "nearest wins" expressible: a method
/// has to be able to loosen a budget its controller declared, which a tightest-wins rule cannot
/// say. Reverse this order and every such method silently inherits its class's number instead.
/// </para>
/// <para>
/// Pinned here rather than asserted through a running handler because the ordering is the
/// generator's, and a runtime test over it would pass or fail for reasons two layers away from the
/// line that decides it.
/// </para>
/// </remarks>
public class HandlerMetadataOrderTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),        // Hardened.Web.Runtime
        typeof(FromBodyAttribute),   // Hardened.Requests.Abstract
        typeof(TimeoutAttribute),    // Hardened.Requests.Runtime
        typeof(EnableAttribute<>)    // Hardened.Shared.Runtime
    ];

    private const string Source = """
        using Hardened.Requests.Runtime.Filters;
        using Hardened.Shared.Runtime.Attributes;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class Application { }

        [BasePath("/rates")]
        [Timeout(Milliseconds = 100)]
        public class RateController {
            [Get("/{symbol}")]
            [Timeout(Milliseconds = 60000)]
            public string Read(string symbol) => symbol;
        }
        """;

    /// <summary>
    /// The emitted metadata array, as written.
    /// </summary>
    private static string MetadataArray() {
        var result = GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = Source },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors).AssertNoErrors();

        // By hint name, which is how the harness addresses a generated file.
        var match = Regex.Match(
            result.SourceContaining("RateController_Read"),
            @"_metadata\s*=\s*new object\[\]\s*\{(.*?)\};",
            RegexOptions.Singleline);

        Assert.True(match.Success, "No metadata array in the generated handler.");

        return match.Groups[1].Value;
    }

    [Fact]
    public void AMethodsOwnAttributesComeBeforeItsClasses() {
        var metadata = MetadataArray();

        var method = metadata.IndexOf("60000", StringComparison.Ordinal);
        var declaringClass = metadata.IndexOf("100", StringComparison.Ordinal);

        Assert.True(method >= 0, "The method's own declaration is missing from the metadata.");
        Assert.True(declaringClass >= 0, "The class's declaration is missing from the metadata.");
        Assert.True(
            method < declaringClass,
            "A method's attributes must precede its class's, or nearest-wins resolution inverts.");
    }

    /// <summary>
    /// The class-level declaration is carried at all, which is the other half of the rung: a
    /// controller bounds every method that says nothing.
    /// </summary>
    [Fact]
    public void AClassLevelDeclarationReachesTheHandler() {
        Assert.Contains("TimeoutAttribute", MetadataArray());
    }
}
