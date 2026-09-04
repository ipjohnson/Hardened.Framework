using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Runtime.Validation;
using Hardened.SourceGeneration.Testing;
using Hardened.Validation.SourceGenerator;
using Hardened.Web.Runtime.Attributes;
using Microsoft.CodeAnalysis;
using ValidationModules;
using Xunit;

namespace Hardened.Web.SourceGenerator.Tests.Validation;

/// <summary>
/// Constraints declared in a project that references no validation generator.
/// </summary>
/// <remarks>
/// One of the five mistakes the 0.19 trial made deliberately, and one of the five that built clean.
/// The constraint attributes come from a package the application already has; compiling them into a
/// validator is a second package, and referencing the first without the second enforces nothing.
/// Code-first is the silent form - the constraints never run. Spec-first is the loud one, where the
/// filter is attached against a validator nobody emitted and every constrained operation answers a
/// 500.
/// </remarks>
public class NoValidationGeneratorTests {
    private const string DiagnosticId = "HRDV006";

    private static readonly Type[] Anchors = [
        typeof(GetAttribute),                     // Hardened.Web.Runtime
        typeof(FromBodyAttribute),                // Hardened.Requests.Abstract
        typeof(ValidationFilterProvider<object>), // Hardened.Requests.Runtime
        typeof(IValidatorFor<object>)             // ValidationModules.Runtime
    ];

    /// <summary>The web generator alone, which is the shape of a consumer who referenced only it.</summary>
    private static GeneratorResult WithoutTheValidationGenerator(string source) =>
        GeneratorTestHarness.Run(source, new WebLibrarySourceGenerator(), Anchors);

    private static GeneratorResult WithTheValidationGenerator(string source) =>
        GeneratorTestHarness.Run(
            new Dictionary<string, string> { ["Test.cs"] = source },
            new IIncrementalGenerator[] {
                new WebLibrarySourceGenerator(), new HardenedValidationGenerator()
            },
            Anchors);

    private static IEnumerable<Diagnostic> Reported(GeneratorResult result) =>
        result.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == DiagnosticId);

    private const string ConstrainedModel = """
        using ValidationModules.Constraints;
        using Hardened.Web.Runtime.Attributes;

        namespace TestApp;

        public class Order {
            [Required]
            [StringLength(3, 20)]
            public string? Reference { get; set; }
        }

        public class OrderController {
            [Post("/orders")]
            public string Create(Order order) => order.Reference ?? "";
        }
        """;

    [Fact]
    public void ConstraintsWithNoValidationGeneratorAreHRDV006() {
        var diagnostic = Assert.Single(Reported(WithoutTheValidationGenerator(ConstrainedModel)));

        Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        Assert.Contains("OrderController.Create", diagnostic.GetMessage());
    }

    /// <summary>
    /// A warning, not an error. The constraints still describe the contract and still reach the
    /// published document, and an assembly declaring models for someone else to validate is a real
    /// arrangement - it just must not be a surprise.
    /// </summary>
    [Fact]
    public void TheReportIsAWarning() {
        Assert.All(
            Reported(WithoutTheValidationGenerator(ConstrainedModel)),
            diagnostic => Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity));
    }

    [Fact]
    public void TheMessageNamesTheGeneratorToReference() {
        var message = Assert.Single(Reported(WithoutTheValidationGenerator(ConstrainedModel))).GetMessage();

        Assert.Contains("Hardened.Validation.SourceGenerator", message);
    }

    [Fact]
    public void ConstraintsWithTheValidationGeneratorReportNothing() {
        Assert.Empty(Reported(WithTheValidationGenerator(ConstrainedModel)));
    }

    /// <summary>A project that declared no constraints never asked for validation.</summary>
    [Fact]
    public void NoConstraintsReportsNothing() {
        Assert.Empty(Reported(WithoutTheValidationGenerator("""
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class Order {
                public string? Reference { get; set; }
            }

            public class OrderController {
                [Post("/orders")]
                public string Create(Order order) => order.Reference ?? "";
            }
            """)));
    }

    /// <summary>A constraint on the parameter itself, which is read through the same front end.</summary>
    [Fact]
    public void AConstraintOnAParameterIsReportedToo() {
        Assert.Single(Reported(WithoutTheValidationGenerator("""
            using ValidationModules.Constraints;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ItemController {
                [Get("/items/{id}")]
                public string ItemById([StringLength(3, 3)] string id) => id;
            }
            """)));
    }

    /// <summary>
    /// One report per assembly. The missing reference is a single thing to fix, and forty
    /// constrained handlers would otherwise produce forty copies of the same sentence.
    /// </summary>
    [Fact]
    public void SeveralConstrainedHandlersAreOneReport() {
        Assert.Single(Reported(WithoutTheValidationGenerator("""
            using ValidationModules.Constraints;
            using Hardened.Web.Runtime.Attributes;

            namespace TestApp;

            public class ItemController {
                [Get("/items/{id}")]
                public string ItemById([StringLength(3, 3)] string id) => id;

                [Get("/items/by-code/{code}")]
                public string ByCode([StringLength(4, 4)] string code) => code;
            }
            """)));
    }

    /// <summary>
    /// Still nothing is attached. The report says the constraints are not enforced; emitting a
    /// filter that names a validator nobody wrote would be the worse answer, and is what the
    /// generator refused to do before it could say why.
    /// </summary>
    [Fact]
    public void NothingIsAttachedEitherWay() {
        var result = WithoutTheValidationGenerator(ConstrainedModel).AssertNoErrors();

        var handler = result.GeneratedSources
            .Single(pair => pair.Key.Contains("OrderController_Create")).Value;

        Assert.DoesNotContain("ValidationFilterProvider", handler);
    }
}
