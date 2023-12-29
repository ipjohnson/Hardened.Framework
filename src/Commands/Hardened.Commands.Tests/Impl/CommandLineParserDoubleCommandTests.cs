using Hardened.Commands.Impl;
using Hardened.Shared.Testing.Attributes;
using NSubstitute;
using Xunit;

namespace Hardened.Commands.Tests.Impl;

public class CommandLineParserDoubleCommandTests {
    
    [HardenedTest]
    public async Task ParseSimpleCommand(
        CommandLineParser parser,
        [Mock] ICommandDefinitionProvider commandDefinitionProvider) {
        var command = AddCommand();
        
        commandDefinitionProvider.ProvideDefinitions().Returns(new[] { command, MathCommand() });

        var result = await parser.ParseCommandLineArguments(new[] { "math", "add", "--x", "1", "--y", "2" });

        Assert.Equal(ParseResultStatus.Success, result.ResultStatus);
        Assert.Same(command, result.CommandTreeNode?.Command);
        Assert.Equal(2, result.Options.Count);
        Assert.True(result.Options.ContainsKey("x"));
        Assert.Equal("1", result.Options["x"][0]);
        Assert.True(result.Options.ContainsKey("y"));
        Assert.Equal("2", result.Options["y"][0]);
    }

    
    [HardenedTest]
    public async Task MissingCommand(
        CommandLineParser parser,
        [Mock] ICommandDefinitionProvider commandDefinitionProvider) {
        var command = AddCommand();
        
        commandDefinitionProvider.ProvideDefinitions().Returns(new[] { command, MathCommand() });

        var result = await parser.ParseCommandLineArguments(new[] { "math", "add", "--x", "1" });

        Assert.Equal(ParseResultStatus.MissingOption, result.ResultStatus);
        Assert.Same(command, result.CommandTreeNode?.Command);
        Assert.Equal(1, result.MissingOptions?.Count);
        Assert.Equal("y", result.MissingOptions?[0].OptionName);
    }

    private CommandDefinition MathCommand() {
        
        return new CommandDefinition(
            "",
            "math",
            null,
            "",
            new CommandOption[] {
            },
            async (provider, options) => 0);
    }
    
    private CommandDefinition AddCommand() {
        return new CommandDefinition(
            "math",
            "add",
            null,
            "",
            new[] {
                new CommandOption("x", CommandOptionType.Number, "",true, false),
                new CommandOption("y", CommandOptionType.Number,"", true, false),
            },
            async (provider, options) => 0);
    }
}