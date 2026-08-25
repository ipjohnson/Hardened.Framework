using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Hardened.Validation.SourceGenerator;
using Microsoft.CodeAnalysis;
using ValidationModules;
using Xunit;

namespace Hardened.Validation.SourceGenerator.Tests;

/// <summary>
/// The generator that turns constraint attributes into validators and registers them.
/// </summary>
/// <remarks>
/// <para>
/// It was at <b>12% line coverage / 3% branch</b>, and the only thing driving it was
/// <c>ValidationAttachmentTests</c> over in the web generator's suite — which runs it as the
/// <em>other half</em> of a pair, to prove the web generator's output compiles beside it. Nothing
/// ran it on its own terms, so its own decisions had never been asserted.
/// </para>
/// <para>
/// Every case ends at <see cref="GeneratorResult.AssertNoErrors"/>, which compiles the generated
/// trees together with the source. Asserting on emitted text alone proves the characters are what
/// was expected, not that a consumer can build — the rule <c>testing-conventions.md</c> §1 exists
/// for.
/// </para>
/// </remarks>
public class ValidationGeneratorTests {

    private static readonly Type[] Anchors = [
        typeof(HardenedModuleAttribute),   // Hardened.Shared.Runtime
        typeof(IValidatorFor<object>)      // ValidationModules.Runtime
    ];

    private static GeneratorResult Run(params (string Name, string Source)[] sources) =>
        GeneratorTestHarness.Run(
            sources.ToDictionary(pair => pair.Name, pair => pair.Source),
            [new HardenedValidationGenerator()],
            Anchors);

    private const string EntryPoint = """
        using Hardened.Shared.Runtime.Attributes;

        namespace TestApp;

        [HardenedModule]
        public partial class Application { }
        """;

    private static string Model(string ns = "TestApp.Models", string name = "Customer") => $$"""
        using ValidationModules.Constraints;

        namespace {{ns}};

        public class {{name}} {
            [Required]
            [StringLength(Min = 1, Max = 64)]
            public string Name { get; set; } = "";
        }
        """;

    private static string RegistrationFile(GeneratorResult result) =>
        result.GeneratedSources
            .Single(pair => pair.Key.Contains("ValidationModule", StringComparison.Ordinal))
            .Value;

    private static bool HasRegistrationFile(GeneratorResult result) =>
        result.GeneratedSources.Any(pair => pair.Key.Contains("ValidationModule", StringComparison.Ordinal));

    #region the marker

    /// <summary>
    /// Says "this generator is running" to the handler generators, which emit calls to validators
    /// this one produces and would otherwise name types nobody declares.
    /// </summary>
    [Fact]
    public void TheMarkerIsEmittedEvenForAnEmptyCompilation() {
        var result = Run(("Empty.cs", "namespace TestApp; public class Nothing { }"));

        Assert.Contains(
            result.GeneratedSources,
            pair => pair.Key.Contains("Marker", StringComparison.Ordinal));

        result.AssertNoErrors();
    }

    [Fact]
    public void TheMarkerDeclaresTheTypeOtherGeneratorsLookFor() {
        var marker = Run(("Empty.cs", "namespace TestApp; public class Nothing { }")).GeneratedSources
            .Single(pair => pair.Key.Contains("Marker", StringComparison.Ordinal)).Value;

        Assert.Contains("namespace Hardened.Validation.Generated", marker);
        Assert.Contains("class ValidationGeneratorMarker", marker);
    }

    /// <summary>
    /// Deliberately plain C#. It lands in whatever project references the generator, and a marker
    /// needing a language version or a nullable context to compile would fail the builds it exists
    /// to keep working.
    /// </summary>
    [Fact]
    public void TheMarkerNeedsNoLanguageFeatures() {
        var marker = Run(("Empty.cs", "namespace TestApp; public class Nothing { }")).GeneratedSources
            .Single(pair => pair.Key.Contains("Marker", StringComparison.Ordinal)).Value;

        Assert.DoesNotContain("#nullable", marker);
        Assert.DoesNotContain("namespace Hardened.Validation.Generated;", marker);
        Assert.Contains("{", marker);
    }

    #endregion

    #region validators

    [Fact]
    public void AConstrainedModelGetsAValidator() {
        var result = Run(("Entry.cs", EntryPoint), ("Customer.cs", Model()));

        Assert.Contains(
            result.GeneratedSources,
            pair => pair.Key.Contains("Customer", StringComparison.Ordinal));

        result.AssertNoErrors();
    }

    [Fact]
    public void AnUnconstrainedModelGetsNoValidator() {
        var result = Run(
            ("Entry.cs", EntryPoint),
            ("Plain.cs", "namespace TestApp.Models; public class Plain { public string Name { get; set; } = \"\"; }"));

        Assert.DoesNotContain(
            result.GeneratedSources,
            pair => pair.Key.Contains("Plain", StringComparison.Ordinal));

        result.AssertNoErrors();
    }

    /// <summary>
    /// <c>System.ComponentModel.DataAnnotations</c> reaches the same IR through the same front-end,
    /// so a model annotated either way produces a validator.
    /// </summary>
    [Fact]
    public void DataAnnotationsProduceAValidatorToo() {
        var result = Run(
            ("Entry.cs", EntryPoint),
            ("Annotated.cs", """
                using System.ComponentModel.DataAnnotations;

                namespace TestApp.Models;

                public class Annotated {
                    [Required]
                    [StringLength(64, MinimumLength = 1)]
                    public string Name { get; set; } = "";
                }
                """));

        Assert.Contains(
            result.GeneratedSources,
            pair => pair.Key.Contains("Annotated", StringComparison.Ordinal));

        result.AssertNoErrors();
    }

    /// <summary>
    /// The hint name is namespace-qualified. <c>AddSource</c> throws on a duplicate, and that
    /// failure takes the whole generator down rather than the second type — so two namespaces each
    /// declaring a <c>Customer</c> is the case that has to work.
    /// </summary>
    [Fact]
    public void TwoModelsOfTheSameNameInDifferentNamespacesBothGetValidators() {
        var result = Run(
            ("Entry.cs", EntryPoint),
            ("First.cs", Model("TestApp.First")),
            ("Second.cs", Model("TestApp.Second")));

        Assert.Equal(
            2,
            result.GeneratedSources.Count(pair => pair.Key.Contains("Customer", StringComparison.Ordinal)));

        result.AssertNoErrors();
    }

    [Fact]
    public void EveryConstrainedModelGetsItsOwnValidator() {
        var result = Run(
            ("Entry.cs", EntryPoint),
            ("Customer.cs", Model(name: "Customer")),
            ("Order.cs", Model(name: "Order")));

        Assert.Contains(result.GeneratedSources, pair => pair.Key.Contains("Customer", StringComparison.Ordinal));
        Assert.Contains(result.GeneratedSources, pair => pair.Key.Contains("Order", StringComparison.Ordinal));

        result.AssertNoErrors();
    }

    /// <summary>
    /// A model the application extends in a second file still gets its validator.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The generator sees one <c>TypeDeclarationSyntax</c> per declaring file and one merged symbol
    /// for all of them, and the model is built from the symbol - so a partial type split across two
    /// files produced two identical models, two identical hint names, and an <c>AddSource</c> that
    /// threw on the second.
    /// </para>
    /// <para>
    /// What made it worth a regression test rather than a fix is where it landed. Roslyn reports a
    /// generator that throws as <c>CS8785</c>, a warning: the build succeeded, <em>every</em>
    /// validator in the compilation was gone rather than just this one, and the application answered
    /// 500 on the first request that validated anything. Models are generated <c>partial</c>, which
    /// is an invitation to write exactly the five lines that triggered it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AModelExtendedInASecondFileStillGetsItsValidator() {
        const string generatedHalf = """
            using ValidationModules.Constraints;

            namespace TestApp.Models;

            public partial class Part {
                [Required]
                [StringLength(Min = 1, Max = 64)]
                public string Sku { get; set; } = "";
            }
            """;

        // What a developer adds. No constraints, no attributes - just a second declaration.
        const string userHalf = """
            namespace TestApp.Models;

            public partial class Part {
                public string Display => Sku.ToUpperInvariant();
            }
            """;

        var result = Run(
            ("Entry.cs", EntryPoint),
            ("Part.g.cs", generatedHalf),
            ("Part.cs", userHalf));

        Assert.Empty(result.DuplicateHintNames);
        Assert.Empty(result.GeneratorExceptions);

        Assert.Single(
            result.GeneratedSources,
            pair => pair.Key.Contains("PartValidator", StringComparison.Ordinal));

        // The registration file is the tell for the wider blast radius: it is emitted from the
        // collected validators, so a generator that died before reaching it leaves none at all.
        Assert.True(HasRegistrationFile(result));

        result.AssertNoErrors();
    }

    /// <summary>
    /// And the same across three declarations, because "the first one wins" has to mean one winner
    /// rather than one loser.
    /// </summary>
    [Fact]
    public void AModelDeclaredInThreeFilesStillGetsExactlyOneValidator() {
        const string part = """
            using ValidationModules.Constraints;

            namespace TestApp.Models;

            public partial class Part {
                [Required]
                public string Sku { get; set; } = "";
            }
            """;

        var result = Run(
            ("Entry.cs", EntryPoint),
            ("Part.g.cs", part),
            ("Part.Display.cs", "namespace TestApp.Models;\n\npublic partial class Part {\n    public int Length => Sku.Length;\n}"),
            ("Part.Equality.cs", "namespace TestApp.Models;\n\npublic partial class Part {\n    public bool IsBlank => Sku.Length == 0;\n}"));

        Assert.Empty(result.DuplicateHintNames);
        Assert.Single(
            result.GeneratedSources,
            pair => pair.Key.Contains("PartValidator", StringComparison.Ordinal));

        result.AssertNoErrors();
    }

    #endregion

    #region registration into the entry point

    /// <summary>
    /// A partial of the entry point class, adding to <c>DependencyRegistry&lt;TEntryPoint&gt;</c> —
    /// the same mechanism the routing table uses, and the reason a consumer calls nothing.
    /// DependencyModules composes sibling modules only through attributes someone writes, so a
    /// module emitted next to the entry point would sit there unreferenced.
    /// </summary>
    [Fact]
    public void ValidatorsAreRegisteredIntoTheEntryPoint() {
        var result = Run(("Entry.cs", EntryPoint), ("Customer.cs", Model()));

        var registration = RegistrationFile(result);

        Assert.Contains("partial class Application", registration);
        Assert.Contains("DependencyRegistry<Application>.Add(ValidationModuleDI)", registration);

        result.AssertNoErrors();
    }

    /// <summary>
    /// Without this the field initializer is trimmed and nothing is ever registered — the same
    /// guard the routing table carries.
    /// </summary>
    [Fact]
    public void TheRegistrationFieldIsHeldAgainstTrimming() {
        Assert.Contains(
            "DynamicDependency",
            RegistrationFile(Run(("Entry.cs", EntryPoint), ("Customer.cs", Model()))));
    }

    /// <summary>
    /// Registered as a type rather than an instance: a generated validator takes the validators for
    /// its nested types as constructor parameters, so the container has to build it.
    /// </summary>
    [Fact]
    public void AValidatorIsRegisteredAsASingletonClosedGeneric() {
        var registration = RegistrationFile(Run(("Entry.cs", EntryPoint), ("Customer.cs", Model())));

        Assert.Contains("AddSingleton<global::ValidationModules.IValidatorFor<", registration);
        Assert.Contains("TestApp.Models.Customer>", registration);
    }

    /// <summary>
    /// Ordered, so the emitted table does not reshuffle between builds — which would turn every
    /// incremental compile into a diff.
    /// </summary>
    [Fact]
    public void RegistrationsAreOrderedDeterministically() {
        var first = RegistrationFile(Run(
            ("Entry.cs", EntryPoint),
            ("B.cs", Model("TestApp.Bravo")),
            ("A.cs", Model("TestApp.Alpha"))));

        var second = RegistrationFile(Run(
            ("Entry.cs", EntryPoint),
            ("A.cs", Model("TestApp.Alpha")),
            ("B.cs", Model("TestApp.Bravo"))));

        Assert.Equal(first, second);
        Assert.True(
            first.IndexOf("Alpha", StringComparison.Ordinal) <
            first.IndexOf("Bravo", StringComparison.Ordinal),
            "registrations are not ordered by namespace");
    }

    /// <summary>
    /// Nothing to register means no file. An empty partial adding an empty method would still be
    /// emitted on every build of every project that has none.
    /// </summary>
    [Fact]
    public void NoValidatorsMeansNoRegistrationFile() {
        var result = Run(
            ("Entry.cs", EntryPoint),
            ("Plain.cs", "namespace TestApp.Models; public class Plain { public string Name { get; set; } = \"\"; }"));

        Assert.False(HasRegistrationFile(result));

        result.AssertNoErrors();
    }

    /// <summary>
    /// A validator is still emitted for a compilation with no entry point — the model is the
    /// developer's either way. Only the registration needs somewhere to land.
    /// </summary>
    [Fact]
    public void AValidatorIsEmittedEvenWithNoEntryPoint() {
        var result = Run(("Customer.cs", Model()));

        Assert.Contains(
            result.GeneratedSources,
            pair => pair.Key.Contains("Customer", StringComparison.Ordinal));

        Assert.False(HasRegistrationFile(result));

        result.AssertNoErrors();
    }

    [Fact]
    public void TheRegistrationFileIsNamedForTheEntryPoint() {
        var result = Run(("Entry.cs", EntryPoint), ("Customer.cs", Model()));

        Assert.Contains(
            result.GeneratedSources,
            pair => pair.Key.Contains("Application.ValidationModule", StringComparison.Ordinal));
    }

    #endregion

    /// <summary>
    /// The whole arrangement, compiled: entry point, several constrained models, both attribute
    /// vocabularies, and the registration that wires them together.
    /// </summary>
    [Fact]
    public void TheWholeArrangementCompiles() {
        Run(
            ("Entry.cs", EntryPoint),
            ("Customer.cs", Model("TestApp.Models", "Customer")),
            ("Order.cs", Model("TestApp.Orders", "Order")),
            ("Annotated.cs", """
                using System.ComponentModel.DataAnnotations;

                namespace TestApp.Annotated;

                public class Person {
                    [Required]
                    public string Name { get; set; } = "";

                    [Range(0, 150)]
                    public int Age { get; set; }
                }
                """))
            .AssertNoErrors();
    }
}
