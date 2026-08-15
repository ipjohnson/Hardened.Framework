using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Abstract.Templates;
using Hardened.SourceGeneration.Testing;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Templates;

/// <summary>
/// What a handler carrying <c>[Output&lt;T&gt;]</c> gets: a factory, and the assignment that
/// makes a model mismatch a build error.
/// </summary>
/// <remarks>
/// <para>
/// The second is the whole point, and the boundary it works across is not where it looks. The
/// attribute's own constraint - <c>where T : IHardenedResponseOutput, new()</c> - catches a type that is
/// not an output or has no parameterless constructor, because the attribute is bound in the final
/// compilation. What nothing catches without help is that the view's <c>TModel</c> matches the
/// handler's return type: the attribute cannot express it, and the generator cannot inspect another
/// generator's output.
/// </para>
/// <para>
/// So the generator emits an assignment the compiler has to bind. These tests declare their views
/// in ordinary source rather than through RazorBlade, because what is being checked is the emitted
/// assignment and the compiler's verdict on it - the same verdict it reaches for a view another
/// generator produced.
/// </para>
/// </remarks>
public class OutputFactoryGeneratorTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),           // Hardened.Web.Runtime
        typeof(FromBodyAttribute),      // Hardened.Requests.Abstract
        typeof(TemplateBaseAttribute)   // Hardened.Requests.Abstract
    ];

    /// <summary>Two views over different models, hand-written so the test owns both ends.</summary>
    private const string Views =
        """
        using System.IO;
        using System.Threading;
        using System.Threading.Tasks;
        using Hardened.Requests.Abstract.Execution;
        using Hardened.Requests.Abstract.Outputs;
        using Hardened.Requests.Abstract.Templates;

        namespace TestApp.Views;

        public record FortunePage(int Count);

        public record Receipt(string Total);

        public abstract class ViewBase<TModel> : IHardenedResponseOutput<TModel> {
            public TModel Model { get; private set; } = default!;

            protected IExecutionContext Context { get; private set; } = default!;

            public bool SupportsContentType(string? accept, IExecutionContext context) => true;

            public Task WriteOutput(IExecutionContext context) {
                Model = (TModel)context.Response.ResponseValue!;
                Context = context;

                return Task.CompletedTask;
            }
        }

        public class Fortunes : ViewBase<FortunePage> { }

        public class Receipts : ViewBase<Receipt> { }
        """;

        private static GeneratorResult Generate(string controller) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> {
                ["Views.cs"] = Views,
                ["Controller.cs"] = $$"""
                    using System.Threading.Tasks;
                    using Hardened.Requests.Abstract.Attributes;
                    using Hardened.Shared.Runtime.Attributes;
                    using Hardened.Web.Runtime.Attributes;
                    using TestApp.Views;

                    namespace TestApp;

                    [HardenedModule]
                    public partial class Application { }

                    public class PageController {
                    {{controller}}
                    }
                    """
            },
            new IIncrementalGenerator[] { new WebLibrarySourceGenerator() },
            Anchors);

    private const string MatchingHandler =
        """
            [Get("/fortunes")]
            [Output<TestApp.Views.Fortunes>]
            public FortunePage GetFortunes() => new(1);
        """;

        /// <summary>
        /// The factory is static and closes over nothing, which is the whole reason the model is
        /// attached after construction rather than passed in.
        /// </summary>
        [Fact]
        public void AFactoryIsEmittedForTheDeclaredView() {
        var source = Generate(MatchingHandler).AssertNoErrors().SourceContaining("GetFortunes");

        Assert.Contains("static _ => new global::TestApp.Views.Fortunes()", source);
        Assert.Contains("context.Response.OutputFactory = _outputFactory", source);
        }

        /// <summary>
        /// And the assignment that binds the view against the handler's return type. It is named after
        /// the handler, because it lands in generated code rather than on the attribute and has to be
        /// traceable back to the declaration that caused it.
        /// </summary>
        [Fact]
        public void ACheckIsEmittedAgainstTheHandlersReturnType() {
        var source = Generate(MatchingHandler).AssertNoErrors().SourceContaining("GetFortunes");

        Assert.Contains("_outputCheck_GetFortunes", source);
        Assert.Contains("IHardenedResponseOutput<global::TestApp.Views.FortunePage>", source);
        }

        /// <summary>
        /// A view over the wrong model does not compile, and the error names both types. This is the
        /// one mechanism that works across a generator boundary: another generator's output cannot be
        /// inspected, but code can be emitted that the compiler binds against it.
        /// </summary>
        [Fact]
        public void AViewOverTheWrongModelIsABuildError() {
        var errors = Generate("""
                [Get("/fortunes")]
                [Output<TestApp.Views.Receipts>]
                public FortunePage GetFortunes() => new(1);
            """).Errors.ToArray();

        Assert.Contains(errors, error =>
            error.GetMessage().Contains("Receipts") &&
            error.GetMessage().Contains("FortunePage"));
        }

        /// <summary>
        /// <c>Task&lt;T&gt;</c> is how a value is returned rather than what it is, and the response
        /// value the handler assigns is the awaited one - so the check is against <c>T</c>. Against the
        /// task it would demand a view over a task and reject every async handler.
        /// </summary>
        [Fact]
        public void AnAsyncHandlerIsCheckedAgainstItsAwaitedType() {
        var source = Generate("""
                [Get("/fortunes")]
                [Output<TestApp.Views.Fortunes>]
                public Task<FortunePage> GetFortunes() => Task.FromResult(new FortunePage(1));
            """).AssertNoErrors().SourceContaining("GetFortunes");

        Assert.Contains("IHardenedResponseOutput<global::TestApp.Views.FortunePage>", source);
        Assert.DoesNotContain("IHardenedResponseOutput<global::System.Threading.Tasks.Task", source);
        }

        /// <summary>
        /// A type that is not an output is rejected on the attribute, by its own constraint - so the
        /// error names the template rather than landing in generated code.
        /// </summary>
        [Fact]
        public void ATypeThatIsNotAnOutputIsRejectedOnTheAttribute() {
        var errors = Generate("""
                [Get("/fortunes")]
                [Output<TestApp.Views.FortunePage>]
                public FortunePage GetFortunes() => new(1);
            """).Errors.ToArray();

        Assert.Contains(errors, error => error.Id == "CS0311" || error.Id == "CS0315");
        }

        /// <summary>
        /// A handler that declares no output gets neither field. The overwhelming majority of handlers
        /// serialize rather than render, and paying an extra static field each would be the wrong
        /// default.
        /// </summary>
        [Fact]
        public void AHandlerWithNoOutputGetsNoFields() {
        var source = Generate("""
                [Get("/plain")]
                public string Plain() => "x";
            """).AssertNoErrors().SourceContaining("Plain");

        Assert.DoesNotContain("_outputFactory", source);
        Assert.DoesNotContain("_outputCheck_", source);
        }
        }
