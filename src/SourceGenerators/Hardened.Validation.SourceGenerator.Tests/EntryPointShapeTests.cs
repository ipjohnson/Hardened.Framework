using Hardened.Shared.Runtime.Attributes;
using Hardened.SourceGeneration.Testing;
using Microsoft.CodeAnalysis;
using ValidationModules;
using Xunit;

namespace Hardened.Validation.SourceGenerator.Tests;

/// <summary>
/// The generator reads the entry point through the shared selector, and the selector walks whatever
/// the class declares.
/// </summary>
/// <remarks>
/// <para>
/// Every other case in this suite uses an entry point that is an empty class, which is the one
/// shape whose member walk does nothing. A real application's entry point carries configuration
/// methods, properties and attributes of its own, and each of those is a separate path through the
/// transform that feeds this generator its model.
/// </para>
/// <para>
/// What is asserted is that the validators still arrive. The entry point's own members are none of
/// this generator's business, so the thing worth pinning is that they do not become its business by
/// accident.
/// </para>
/// </remarks>
public class EntryPointShapeTests
{
    private static readonly Type[] Anchors =
    [
        typeof(HardenedModuleAttribute),
        typeof(IValidatorFor<object>),
    ];

    private static GeneratorResult Run(params (string Name, string Source)[] sources) =>
        GeneratorTestHarness.Run(
            sources.ToDictionary(pair => pair.Name, pair => pair.Source),
            [new HardenedValidationGenerator()],
            Anchors
        );

    private const string ConstrainedModel = """
        using ValidationModules.Constraints;

        namespace TestApp.Models;

        public class Customer
        {
            [Required]
            [StringLength(Min = 1, Max = 64)]
            public string Name { get; set; } = "";
        }
        """;

    private static string RegistrationFile(GeneratorResult result) =>
        result
            .GeneratedSources.Single(pair =>
                pair.Key.Contains("ValidationModule", StringComparison.Ordinal)
            )
            .Value;

    [Fact]
    public void AnEntryPointDeclaringMethods_StillRegistersValidators()
    {
        const string entryPoint = """
            using Hardened.Shared.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestApp;

            [HardenedModule]
            public partial class Application
            {
                public static void Configure(IServiceCollection services) { }

                private int Count(string value) => value.Length;

                public string Describe() => "an application";
            }
            """;

        var result = Run(("Entry.cs", entryPoint), ("Customer.cs", ConstrainedModel));

        Assert.Contains("partial class Application", RegistrationFile(result));

        result.AssertNoErrors();
    }

    [Fact]
    public void AnEntryPointDeclaringProperties_StillRegistersValidators()
    {
        const string entryPoint = """
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [HardenedModule]
            public partial class Application
            {
                public string Name { get; set; } = "";

                public int Retries { get; init; }

                private static bool Ready => true;
            }
            """;

        var result = Run(("Entry.cs", entryPoint), ("Customer.cs", ConstrainedModel));

        Assert.Contains("partial class Application", RegistrationFile(result));

        result.AssertNoErrors();
    }

    /// <summary>
    /// An attribute carrying arguments, which is a different path through the attribute reader than
    /// the bare marker every other case uses.
    /// </summary>
    [Fact]
    public void AnEntryPointCarryingAttributeArguments_StillRegistersValidators()
    {
        const string entryPoint = """
            using System;
            using Hardened.Shared.Runtime.Attributes;

            namespace TestApp;

            [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
            public class LabelAttribute : Attribute
            {
                public LabelAttribute(string name)
                {
                    Name = name;
                }

                public string Name { get; }

                public int Order { get; set; }
            }

            [HardenedModule]
            [Label("orders", Order = 2)]
            [Label("billing")]
            public partial class Application { }
            """;

        var result = Run(("Entry.cs", entryPoint), ("Customer.cs", ConstrainedModel));

        Assert.Contains("partial class Application", RegistrationFile(result));

        result.AssertNoErrors();
    }

    /// <summary>
    /// Members and attributes together, which is what a scaffolded application actually looks like.
    /// </summary>
    [Fact]
    public void AnEntryPointWithMembersAndAttributes_StillRegistersValidators()
    {
        const string entryPoint = """
            using System;
            using Hardened.Shared.Runtime.Attributes;
            using Microsoft.Extensions.DependencyInjection;

            namespace TestApp;

            [AttributeUsage(AttributeTargets.Class)]
            public class LabelAttribute : Attribute
            {
                public LabelAttribute(string name) { }
            }

            [HardenedModule]
            [Label("orders")]
            public partial class Application
            {
                public string Name { get; set; } = "";

                public static void Configure(IServiceCollection services) { }
            }
            """;

        var result = Run(("Entry.cs", entryPoint), ("Customer.cs", ConstrainedModel));

        Assert.Contains(
            "DependencyRegistry<Application>.Add(ValidationModuleDI)",
            RegistrationFile(result)
        );

        result.AssertNoErrors();
    }
}
