using Hardened.Requests.Abstract.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Compression;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Compression;

/// <summary>
/// <c>[Compress]</c> and <c>[Compress&lt;T&gt;]</c> as the generator re-emits them into a
/// handler's metadata, and the one arrangement it refuses.
///
/// <para>
/// The attribute needs nothing new from the generator: a generic attribute keeps its type
/// argument, a literal is copied through, an enum member is qualified, and a constructor argument
/// and a property initializer are emitted separately. What is worth pinning is that all of that
/// holds for a <c>params object[]</c> constructor carrying an integer, which no other filter
/// attribute has.
/// </para>
/// </summary>
public class CompressAttributeGenerationTests {
    private const string DiagnosticId = "HRDW003";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),       // Hardened.Web.Runtime
        typeof(CompressAttribute),  // Hardened.Web.Runtime.Compression
        typeof(FromBodyAttribute)   // Hardened.Requests.Abstract
    ];

    private static GeneratorResult Generate(string classAttributes, string methodAttributes) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Test.cs"] = $$"""
                    using System;
                    using System.Collections.Generic;
                    using Hardened.Requests.Abstract.Compression;
                    using Hardened.Requests.Abstract.Execution;
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using Hardened.Web.Runtime.Compression;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class TestApplication { }

                    public sealed class ListLargerThan : ICompressionPredicate {
                        private readonly int _count;

                        private ListLargerThan(int count) => _count = count;

                        public static ICompressionPredicate Create(object[] args) => args is [int count]
                            ? new ListLargerThan(count)
                            : throw new ArgumentException("ListLargerThan takes one integer.");

                        public bool ShouldCompress(object value, IExecutionContext context) =>
                            value is System.Collections.ICollection { Count: var n } && n > _count;
                    }

                    {{classAttributes}}
                    public class PetsController {
                        [Get("/pets")]
                        {{methodAttributes}}
                        public List<string> List() => new();
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private static IEnumerable<Diagnostic> Reported(string classAttributes, string methodAttributes) =>
        Generate(classAttributes, methodAttributes).GeneratorDiagnostics
            .Where(reported => reported.Id == DiagnosticId);

    [Fact]
    public void ThePredicateFormWithAnIntegerAndAFavourCompiles() {
        var result = Generate("", "[Compress<ListLargerThan>(50, Favor = CompressionType.Br)]")
            .AssertNoErrors();

        var source = result.SourceContaining("PetsController_List");

        Assert.Contains("global::Hardened.Web.Runtime.Compression.CompressAttribute<global::TestApp.ListLargerThan>(50)", source);
        Assert.Contains("Favor = global::Hardened.Requests.Abstract.Compression.CompressionType.Br", source);
    }

    [Fact]
    public void ThePlainFormOnAMethodCompiles() {
        var result = Generate("", "[Compress(Favor = CompressionType.GZip)]").AssertNoErrors();

        var source = result.SourceContaining("PetsController_List");

        Assert.Contains("global::Hardened.Web.Runtime.Compression.CompressAttribute()", source);
        Assert.Contains("Favor = global::Hardened.Requests.Abstract.Compression.CompressionType.GZip", source);
    }

    [Fact]
    public void ThePlainFormOnAClassReachesEveryHandlersMetadata() {
        var result = Generate("[Compress]", "").AssertNoErrors();

        Assert.Contains("global::Hardened.Web.Runtime.Compression.CompressAttribute()", result.SourceContaining("PetsController_List"));
    }

    [Theory]
    [InlineData("[Compress]", "")]
    [InlineData("", "[Compress]")]
    [InlineData("", "[Compress<ListLargerThan>(2)]")]
    [InlineData("[Compress<ListLargerThan>(2)]", "")]
    public void OneDeclarationIsNotReported(string classAttributes, string methodAttributes) {
        Generate(classAttributes, methodAttributes).AssertNoErrors();

        Assert.Empty(Reported(classAttributes, methodAttributes));
    }

    /// <summary>
    /// The compiler refuses two of the same form on one element and cannot see across the class
    /// and the method. At run time the method's filter would win silently.
    /// </summary>
    [Theory]
    [InlineData("[Compress]", "[Compress<ListLargerThan>(2)]")]
    [InlineData("[Compress<ListLargerThan>(2)]", "[Compress]")]
    [InlineData("[Compress]", "[Compress]")]
    [InlineData("", "[Compress] [Compress<ListLargerThan>(2)]")]
    public void ADeclarationOnBothTheClassAndTheMethodIsAnError(string classAttributes, string methodAttributes) {
        var diagnostic = Assert.Single(Reported(classAttributes, methodAttributes));

        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("PetsController.List", diagnostic.GetMessage());
        Assert.Contains("2 [Compress] declarations", diagnostic.GetMessage());
    }
}
