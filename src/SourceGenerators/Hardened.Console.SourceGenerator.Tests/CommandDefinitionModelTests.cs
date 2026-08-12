using CSharpAuthor;
using Hardened.Console.SourceGenerator.Impl;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.Console.SourceGenerator.Tests;

/// <summary>
/// <see cref="CommandDefinitionModel"/> is the generator's incremental cache key. Roslyn re-runs the
/// source output only when the model changes, so a model that compares equal to one it should not
/// serves stale generated code after a real edit, and a model that never compares equal makes the
/// IDE regenerate on every keystroke.
/// </summary>
public class CommandDefinitionModelTests {

    private static CommandDefinitionModel Model(
        string commandType = "AddCommand",
        string? parentType = null,
        string commandName = "add",
        string? parentName = "math",
        string? description = "Add two numbers",
        params string[] optionNames) =>
        new(
            TypeDefinition.Get("TestApp", commandType),
            parentType == null ? null : TypeDefinition.Get("TestApp", parentType),
            commandName,
            parentName,
            description,
            optionNames
                .Select(name => new CommandOptionModel(
                    name, name, TypeDefinition.Get(typeof(string)), "String", "", "", false, true))
                .ToArray());

    [Fact]
    public void TwoModelsDescribingTheSameCommandAreEqual() {
        Assert.Equal(Model(optionNames: "x"), Model(optionNames: "x"));
    }

    [Fact]
    public void RenamingTheCommandMakesTheModelDifferent() {
        Assert.NotEqual(Model(commandName: "add"), Model(commandName: "subtract"));
    }

    [Fact]
    public void ReparentingTheCommandMakesTheModelDifferent() {
        Assert.NotEqual(Model(parentName: "math"), Model(parentName: "text"));
    }

    /// <summary>
    /// The description is emitted into the help output, so an edit to it has to invalidate the
    /// cache even though nothing structural changed.
    /// </summary>
    [Fact]
    public void ChangingTheDescriptionMakesTheModelDifferent() {
        Assert.NotEqual(Model(description: "Add"), Model(description: "Add two numbers"));
    }

    [Fact]
    public void ChangingTheCommandTypeMakesTheModelDifferent() {
        Assert.NotEqual(Model(commandType: "AddCommand"), Model(commandType: "SubtractCommand"));
    }

    /// <summary>
    /// The base type drives whether a binder delegates to a parent binder, so gaining one is a
    /// change even when nothing else moved.
    /// </summary>
    [Fact]
    public void GainingABaseTypeMakesTheModelDifferent() {
        Assert.NotEqual(Model(), Model(parentType: "MathCommand"));
    }

    [Fact]
    public void AddingAnOptionMakesTheModelDifferent() {
        Assert.NotEqual(Model(optionNames: ["x"]), Model(optionNames: ["x", "y"]));
    }

    [Fact]
    public void RenamingAnOptionMakesTheModelDifferent() {
        Assert.NotEqual(Model(optionNames: "x"), Model(optionNames: "z"));
    }

    [Fact]
    public void EqualModelsShareAHashCode() {
        Assert.Equal(Model(optionNames: "x").GetHashCode(), Model(optionNames: "x").GetHashCode());
    }

    [Fact]
    public void TheComparerAgreesWithTheModelsOwnEquality() {
        var comparer = new CommandDefinitionModelComparer();

        Assert.True(comparer.Equals(Model(optionNames: "x"), Model(optionNames: "x")));
        Assert.False(comparer.Equals(Model(commandName: "add"), Model(commandName: "subtract")));
        Assert.Equal(
            comparer.GetHashCode(Model(optionNames: "x")),
            comparer.GetHashCode(Model(optionNames: "x")));
    }

    /// <summary>
    /// Roslyn hands the comparer nulls when a syntax node stops producing a model. Both-null is the
    /// same absent command; one-null is not.
    /// </summary>
    [Fact]
    public void TheComparerTreatsTwoAbsentModelsAsEqual() {
        var comparer = new CommandDefinitionModelComparer();

        Assert.True(comparer.Equals(null, null));
        Assert.False(comparer.Equals(Model(), null));
        Assert.False(comparer.Equals(null, Model()));
    }

    /// <summary>
    /// An edit that cannot change any command — a method body elsewhere in the file — must not make
    /// Roslyn regenerate. This is the behaviour the comparer exists for, measured end to end rather
    /// than by inspecting the model.
    /// </summary>
    [Fact]
    public void AnEditThatTouchesNoCommandReusesTheCachedOutput() {
        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> {
                ["Commands.cs"] = ConsoleGeneratorTest.WithApplication(Unrelated("first")),
                ["EntryPointSupport.cs"] = ConsoleGeneratorTest.EntryPointSupport
            },
            new Dictionary<string, string> {
                ["Commands.cs"] = ConsoleGeneratorTest.WithApplication(Unrelated("second")),
                ["EntryPointSupport.cs"] = ConsoleGeneratorTest.EntryPointSupport
            },
            [new ConsoleSourceGenerator()],
            [
                typeof(Commands.Attributes.CommandAttribute),
                typeof(Shared.Runtime.Attributes.HardenedModuleAttribute)
            ]);

        Assert.Equal(result.FirstRun, result.SecondRun);
        Assert.True(
            result.AllOutputsCached,
            "The generator regenerated after an edit that could not change a command: " +
            string.Join(", ", result.OutputReasons));
    }

    /// <summary>The counterpart: renaming a command does have to regenerate.</summary>
    [Fact]
    public void RenamingACommandChangesTheGeneratedOutput() {
        var result = GeneratorTestHarness.RunIncremental(
            new Dictionary<string, string> {
                ["Commands.cs"] = ConsoleGeneratorTest.WithApplication(Renamed("greet")),
                ["EntryPointSupport.cs"] = ConsoleGeneratorTest.EntryPointSupport
            },
            new Dictionary<string, string> {
                ["Commands.cs"] = ConsoleGeneratorTest.WithApplication(Renamed("welcome")),
                ["EntryPointSupport.cs"] = ConsoleGeneratorTest.EntryPointSupport
            },
            [new ConsoleSourceGenerator()],
            [
                typeof(Commands.Attributes.CommandAttribute),
                typeof(Shared.Runtime.Attributes.HardenedModuleAttribute)
            ]);

        Assert.Contains("\"greet\"", Assert.Single(result.FirstRun).Value);
        Assert.Contains("\"welcome\"", Assert.Single(result.SecondRun).Value);
    }

    private static string Unrelated(string returnValue) =>
        $$"""
        [Command("greet")]
        public class GreetCommand {
            public string Name { get; set; } = "";
        }

        public class Helper {
            public string Describe() => "{{returnValue}}";
        }
        """;

    private static string Renamed(string commandName) =>
        $$"""
        [Command("{{commandName}}")]
        public class GreetCommand {
            public string Name { get; set; } = "";
        }
        """;
}
