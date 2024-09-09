using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Commands.Impl;

[Expose]
public class CommandLineApplicationDelegateProvider : IApplicationDelegateProvider {
    private readonly ICommandLineParser _commandLineParser;
    private readonly ICommandLinePrinter _commandLinePrinter;

    public CommandLineApplicationDelegateProvider(ICommandLineParser commandLineParser, ICommandLinePrinter commandLinePrinter) {
        _commandLineParser = commandLineParser;
        _commandLinePrinter = commandLinePrinter;
    }

    public async Task<ApplicationDelegate> ProvideDelegate(IHardenedEnvironment environment, IServiceProvider serviceProvider) {
        var results = await _commandLineParser.ParseCommandLineArguments(environment.Arguments);

        if (results is {
                ResultStatus: ParseResultStatus.Success, 
                CommandTreeNode.Command.RunCommandDelegate: not null
            }) {
            return new ApplicationDelegate(
                () => results.CommandTreeNode.Command.RunCommandDelegate(serviceProvider, results.Options), 
                true);
        }
        
        return new ApplicationDelegate(
            async () => {
                await _commandLinePrinter.PrintParseResult(results);
                
                return 1;
            },
            false
        );
    }
}