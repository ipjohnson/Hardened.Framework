using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Validation;
using Hardened.SourceGeneration.Testing;
using Hardened.Validation.SourceGenerator;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using ValidationModules;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests;

/// <summary>
/// The two generators that have to agree, run together the way a real project runs them.
/// </summary>
/// <remarks>
/// <para>
/// The web generator emits a validator for a handler's <c>Parameters</c> class that calls the
/// validator the validation generator emits for the model - a type it cannot see, named by
/// convention. That is the whole risk in the arrangement, and running either generator alone hides
/// it: the web generator's output looks fine on its own and does not compile beside the other's.
/// So every case here ends at <see cref="GeneratorResult.AssertNoErrors"/>, which compiles both
/// generators' output together with the source.
/// </para>
/// </remarks>
public class ValidationAttachmentTests {

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),                    // Hardened.Web.Runtime
        typeof(FromBodyAttribute),               // Hardened.Requests.Abstract
        typeof(ValidationFilterProvider<object>),// Hardened.Requests.Runtime
        typeof(IValidatorFor<object>)            // ValidationModules.Runtime
    ];

    /// <summary>
    /// The handler class for <paramref name="name"/>, rather than the validator emitted beside it.
    /// </summary>
    /// <remarks>
    /// Both files are named after the handler, so a substring match on the hint name finds two.
    /// The validator is the one that says so.
    /// </remarks>
    private static string Handler(GeneratorResult result, string name) {
        var matches = result.GeneratedSources
            .Where(pair => pair.Key.Contains(name) && !pair.Key.Contains("ParametersValidator"))
            .ToArray();

        Assert.True(matches.Length == 1,
            $"Expected exactly one handler file for '{name}', found {matches.Length}: " +
            string.Join(", ", matches.Select(pair => pair.Key)));

        return matches[0].Value;
    }

    private static GeneratorResult Generate(string source) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new IIncrementalGenerator[] {
                new WebLibrarySourceGenerator(), new HardenedValidationGenerator()
            },
            Anchors);

    private const string ConstrainedModel = """
        using System.Threading.Tasks;
        using ValidationModules.Constraints;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        public class Order {
            [Required]
            [StringLength(3, 20)]
            public string? Reference { get; set; }
        }

        """;

    /// <summary>
    /// The defect this closes. Nothing in the controller mentions validation, and the handler ends
    /// up carrying a filter that runs the model's constraints.
    /// </summary>
    [Fact]
    public void AConstrainedBodyModelAttachesAFilter() {
        var result = Generate(ConstrainedModel + """
            public class OrderController {
                [Post("/orders")]
                public string Create(Order order) => order.Reference ?? "";
            }
            """).AssertNoErrors();

        Assert.Contains("ValidationFilterProvider<Parameters>", Handler(result, "OrderController_Create"));
    }

    /// <summary>
    /// The parameters validator descends rather than checking anything itself: the constraints stay
    /// on the model, and its validator is the one that evaluates them.
    /// </summary>
    [Fact]
    public void TheParametersValidatorDelegatesToTheModelValidator() {
        var result = Generate(ConstrainedModel + """
            public class OrderController {
                [Post("/orders")]
                public string Create(Order order) => order.Reference ?? "";
            }
            """).AssertNoErrors();

        var validator = result.SourceContaining("ParametersValidator");

        Assert.Contains("global::TestApp.OrderValidator.Instance", validator);
        Assert.Contains("ctx.Push(\"order\")", validator);
    }

    /// <summary>
    /// A handler whose types constrain nothing gets nothing. Attaching everywhere would put the
    /// cost on requests with nothing to check, and would make the case above unfalsifiable.
    /// </summary>
    [Fact]
    public void AnUnconstrainedHandlerGetsNoFilter() {
        var result = Generate("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class Plain { public string? Name { get; set; } }

            public class PlainController {
                [Post("/plain")]
                public string Create(Plain plain) => plain.Name ?? "";
            }
            """).AssertNoErrors();

        Assert.DoesNotContain("ValidationFilterProvider", Handler(result, "PlainController_Create"));
    }

    /// <summary>
    /// Without the validation generator, nothing emits validators - so attaching one would name a
    /// type that does not exist and fail the build of a project that never asked for validation.
    /// </summary>
    /// <remarks>
    /// The web generator asks rather than assumes, through a marker the validation generator
    /// declares in post-initialization output. This runs the web generator alone, which is exactly
    /// the shape of a consumer who references it and not the other.
    /// </remarks>
    [Fact]
    public void WithoutTheValidationGeneratorNothingIsAttached() {
        var result = GeneratorTestHarness.Run(
            ConstrainedModel + """
            public class OrderController {
                [Post("/orders")]
                public string Create(Order order) => order.Reference ?? "";
            }
            """,
            new WebLibrarySourceGenerator(),
            Anchors).AssertNoErrors();

        Assert.DoesNotContain("ValidationFilterProvider", Handler(result, "OrderController_Create"));
    }

    /// <summary>
    /// A constraint on the parameter itself is not compiled, and says so. The alternative is the
    /// failure this design refuses everywhere else - a constraint that was declared, never
    /// evaluated, and never mentioned.
    /// </summary>
    [Fact]
    public void AConstraintOnAParameterIsReported() {
        var result = Generate("""
            using ValidationModules.Constraints;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ItemController {
                [Get("/items/{id}")]
                public string ItemById([StringLength(3, 3)] string id) => id;
            }
            """);

        Assert.Contains(result.GeneratorDiagnostics, diagnostic => diagnostic.Id == "HRDV001");
    }

    /// <summary>
    /// And it does not take over the parameter's binding on the way. Every attribute the generator
    /// does not recognise is emitted as a custom binder, so before constraints were recognised this
    /// handler bound <c>id</c> from a <c>StringLength</c> attribute rather than from the route.
    /// </summary>
    [Fact]
    public void AConstraintOnAParameterDoesNotChangeHowItBinds() {
        var result = Generate("""
            using ValidationModules.Constraints;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ItemController {
                [Get("/items/{id}")]
                public string ItemById([StringLength(3, 3)] string id) => id;
            }
            """).AssertNoErrors();

        var handler = Handler(result, "ItemController_ItemById");

        Assert.Contains("PathTokens", handler);
        Assert.DoesNotContain("CustomAttributeData", handler);
    }
}
