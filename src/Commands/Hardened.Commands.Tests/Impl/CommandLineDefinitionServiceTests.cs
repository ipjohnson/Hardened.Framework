using Hardened.Commands.Impl;
using NSubstitute;
using Xunit;

namespace Hardened.Commands.Tests.Impl;

/// <summary>
/// The command tree the parser walks. Every generated <c>CommandDefinitionProvider</c> feeds this
/// service, so a tree built wrong makes commands unreachable rather than merely mis-parsed.
/// </summary>
public class CommandLineDefinitionServiceTests {

    /// <summary>
    /// With no command named "" the service invents a root so the tree always has one, and the
    /// declared commands hang off it.
    /// </summary>
    [Fact]
    public void CommandsWithNoParentHangOffASyntheticRoot() {
        var tree = CommandLineTest.DefinitionService(
            CommandLineTest.Command("greet"),
            CommandLineTest.Command("version")).GetTree();

        Assert.Equal("", tree.Command.CommandName);
        Assert.Null(tree.Command.RunCommandDelegate);
        Assert.Equal(
            ["greet", "version"],
            tree.ChildCommands.Select(node => node.Command.CommandName).Order());
    }

    /// <summary>
    /// A declared unnamed command becomes the root, delegate and all — that is what makes the
    /// application runnable with no command on the command line.
    /// </summary>
    [Fact]
    public void AnUnnamedCommandBecomesTheRoot() {
        var root = CommandLineTest.RootCommand(CommandLineTest.Required("name"));

        var tree = CommandLineTest.DefinitionService(root).GetTree();

        Assert.Same(root, tree.Command);
        Assert.NotNull(tree.Command.RunCommandDelegate);
    }

    [Fact]
    public void ASubcommandIsPlacedUnderItsParent() {
        var tree = CommandLineTest.DefinitionService(
            CommandLineTest.Command("math"),
            CommandLineTest.SubCommand("math", "add")).GetTree();

        var math = Assert.Single(tree.ChildCommands);

        Assert.Equal("add", Assert.Single(math.ChildCommands).Command.CommandName);
    }

    /// <summary>
    /// Both the parser and the printer walk upwards — for inherited options and for the usage line
    /// respectively — so every node has to know its parent.
    /// </summary>
    [Fact]
    public void EveryNodeKnowsItsParent() {
        var tree = CommandLineTest.DefinitionService(
            CommandLineTest.Command("math"),
            CommandLineTest.SubCommand("math", "add")).GetTree();

        var math = Assert.Single(tree.ChildCommands);
        var add = Assert.Single(math.ChildCommands);

        Assert.Same(tree, math.ParentCommand);
        Assert.Same(math, add.ParentCommand);
        Assert.Null(tree.ParentCommand);
    }

    /// <summary>
    /// The tree is built once. Every parse asks for it, and rebuilding would re-run every
    /// generated provider on each invocation.
    /// </summary>
    [Fact]
    public void TheTreeIsBuiltOnceAndReused() {
        var provider = Substitute.For<ICommandDefinitionProvider>();
        provider.ProvideDefinitions().Returns([CommandLineTest.Command("greet")]);

        var service = CommandLineTest.DefinitionServiceOver(provider);

        Assert.Same(service.GetTree(), service.GetTree());
        provider.Received(1).ProvideDefinitions();
    }

    /// <summary>
    /// Definitions from every registered provider land in one tree — an application composed of
    /// several modules gets all of their commands, not the first module's.
    /// </summary>
    [Fact]
    public void CommandsFromEveryProviderReachTheTree() {
        var tree = CommandLineTest.DefinitionServiceOver(
            CommandLineTest.Provider(CommandLineTest.Command("greet")),
            CommandLineTest.Provider(CommandLineTest.Command("version"))).GetTree();

        Assert.Equal(
            ["greet", "version"],
            tree.ChildCommands.Select(node => node.Command.CommandName).Order());
    }

    /// <summary>
    /// Two commands claiming the same name is not resolvable — one of them would silently never
    /// run — so it fails at tree construction rather than at the first invocation.
    /// </summary>
    [Fact]
    public void TwoCommandsWithTheSameNameAreRejected() {
        var service = CommandLineTest.DefinitionService(
            CommandLineTest.Command("greet"),
            CommandLineTest.Command("greet"));

        Assert.Contains("Duplicate greet", Assert.Throws<Exception>(() => service.GetTree()).Message);
    }

    /// <summary>A subcommand naming a parent that was never declared is rejected by name.</summary>
    [Fact]
    public void ASubcommandOfACommandThatDoesNotExistIsRejected() {
        var service = CommandLineTest.DefinitionService(
            CommandLineTest.SubCommand("nonexistent", "add"));

        Assert.Contains(
            "Parent command nonexistent not found",
            Assert.Throws<Exception>(() => service.GetTree()).Message);
    }

    /// <summary>
    /// Only one command can be the unnamed root. Two would make which one runs depend on provider
    /// ordering.
    /// </summary>
    [Fact]
    public void TwoRootCommandsAreRejected() {
        var service = CommandLineTest.DefinitionService(
            CommandLineTest.RootCommand(),
            CommandLineTest.RootCommand());

        Assert.Contains(
            "Only one root command can exist",
            Assert.Throws<Exception>(() => service.GetTree()).Message);
    }

    /// <summary>An application that declares no commands still produces a tree to print help from.</summary>
    [Fact]
    public void AnApplicationWithNoCommandsStillProducesARoot() {
        var tree = CommandLineTest.DefinitionServiceOver().GetTree();

        Assert.Equal("", tree.Command.CommandName);
        Assert.Empty(tree.ChildCommands);
    }
}
