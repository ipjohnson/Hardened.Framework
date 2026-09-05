using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Templates;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Templates;

/// <summary>
/// <c>[Enable&lt;T&gt;]</c> and the abstract template base it produces.
/// </summary>
/// <remarks>
/// <para>
/// The marker is resolved and read, never recognised by name. These tests declare their own
/// markers and bases rather than using <c>RazorTemplates</c>, which is the assertion that
/// matters: a package the generator has never heard of gets the same treatment, so a Fluid or
/// Mustache engine needs no change here. A test using Hardened's own marker would pass equally
/// well against a generator that switched on its name.
/// </para>
/// </remarks>
public class TemplateBaseGeneratorTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),           // Hardened.Web.Runtime
        typeof(FromBodyAttribute),      // Hardened.Requests.Abstract
        typeof(TemplateBaseAttribute)   // Hardened.Requests.Abstract, the marker vocabulary
    ];

    /// <summary>A template engine of a third party's own making.</summary>
    private const string Engine =
        """
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Execution;
        using Hardened.Requests.Abstract.Outputs;
        using Hardened.Requests.Abstract.Templates;

        namespace Other.Engine;

        public abstract class FluidBase<TModel> : IHardenedResponseOutput<TModel> {
            public TModel Model { get; private set; } = default!;

            // Part of the contract [TemplateBase] declares, not of IHardenedResponseOutput: the
            // generated base puts a Links property on every view and resolves it from here.
            protected IExecutionContext Context { get; private set; } = default!;

            public virtual string ContentType => "text/plain";

            public bool SupportsContentType(string? accept, IExecutionContext context) => true;

            public Task WriteOutput(IExecutionContext context) {
                Model = (TModel)context.Response.ResponseValue!;
                Context = context;

                return Task.CompletedTask;
            }
        }

        [TemplateBase(typeof(FluidBase<>))]
        [TemplateContentType("text/html; charset=utf-8")]
        public sealed class FluidTemplate { }

        [TemplateBase(typeof(FluidBase<>))]
        [TemplateContentType("text/csv")]
        public sealed class CsvTemplate { }

        public sealed class SomeOtherFeature { }
        """;

        private static GeneratorResult Generate(string application) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Engine.cs"] = Engine,
                ["Application.cs"] = application
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors).AssertNoErrors();

        /// <summary>An entry point named <paramref name="name"/> enabling the named markers.</summary>
        private static string Application(string name, params string[] markers) =>
        $$"""
        using Hardened.Shared.Runtime.Attributes;
        using Other.Engine;

        namespace TestApp;

        [HardenedModule]
        {{string.Join("\n", markers.Select(marker => $"[Enable<{marker}>]"))}}
        public partial class {{name}} { }
        """;

        /// <summary>
        /// The generated base derives from what the marker's <c>[TemplateBase]</c> names, closed over
        /// the generated class's own type parameter - so a view writing
        /// <c>@inherits ApplicationFluidTemplate&lt;FortunePage&gt;</c> gets a typed <c>Model</c>.
        /// </summary>
        [Fact]
        public void TheBaseDerivesFromWhatTheMarkerNames() {
        var source = Generate(Application("Application", "FluidTemplate")).SourceContaining("FluidTemplate");

        Assert.Contains("class ApplicationFluidTemplate<TModel>", source);
        Assert.Contains("FluidBase<TModel>", source);
        }

        /// <summary>
        /// The name is scoped to the entry point. An assembly with two modules would otherwise have two
        /// generator runs racing for one type name, which is the same reason the routing table is
        /// <c>Application.RoutingTable</c>.
        /// </summary>
        [Fact]
        public void TheNameIsScopedToTheEntryPoint() {
        var source = Generate(Application("Storefront", "FluidTemplate")).SourceContaining("FluidTemplate");

        Assert.Contains("class StorefrontFluidTemplate<TModel>", source);
        }

        /// <summary>
        /// The content type comes off the marker, not out of the generator. It is what decides whether
        /// a template response is what the client asked for.
        /// </summary>
        [Fact]
        public void TheContentTypeComesFromTheMarker() {
        var source = Generate(Application("Application", "FluidTemplate")).SourceContaining("FluidTemplate");

        Assert.Contains("\"text/html; charset=utf-8\"", source);
        }

        /// <summary>
        /// Nothing is emitted without the attribute. The feature is off until a module asks for it,
        /// which is the whole point of naming the marker rather than probing for the package.
        /// </summary>
        [Fact]
        public void NothingIsEmittedForAModuleThatDidNotEnableIt() {
        var result = Generate(Application("Application"));

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("Fluid"));
        }

        /// <summary>
        /// Two markers on one module produce two bases. Multi-engine falls out of deriving the name
        /// from the marker rather than being retrofitted onto a single generated type.
        /// </summary>
        [Fact]
        public void TwoMarkersProduceTwoBases() {
        var result = Generate(Application("Application", "FluidTemplate", "CsvTemplate"));

        Assert.Contains("class ApplicationFluidTemplate<TModel>", result.SourceContaining("FluidTemplate"));
        Assert.Contains("class ApplicationCsvTemplate<TModel>", result.SourceContaining("CsvTemplate"));
        Assert.Contains("\"text/csv\"", result.SourceContaining("CsvTemplate"));
        }

        /// <summary>
        /// A marker declaring no template base is some other kind of feature, and is skipped rather
        /// than reported. One attribute name serves every optional feature - that is what makes
        /// <c>[Enable&lt;</c> a single thing to remember - so the template generator seeing one it has
        /// no interest in is ordinary.
        /// </summary>
        [Fact]
        public void AMarkerWithNoTemplateBaseIsIgnored() {
        var result = Generate(Application("Application", "SomeOtherFeature"));

        Assert.DoesNotContain(result.GeneratedSources.Keys, key => key.Contains("SomeOtherFeature"));
        }

        /// <summary>
        /// The qualified spelling is the same attribute. A generator recognising only the short form
        /// would silently do nothing for a project that wrote the other, which is the worst shape a
        /// feature switch can fail in.
        /// </summary>
        [Fact]
        public void TheQualifiedSpellingIsRecognised() {
        var result = Generate("""
            namespace TestApp;

            [Hardened.Shared.Runtime.Attributes.HardenedModule]
            [Hardened.Shared.Runtime.Attributes.EnableAttribute<Other.Engine.FluidTemplate>]
            public partial class Application { }
            """);

        Assert.Contains("class ApplicationFluidTemplate<TModel>", result.SourceContaining("FluidTemplate"));
        }
        }
