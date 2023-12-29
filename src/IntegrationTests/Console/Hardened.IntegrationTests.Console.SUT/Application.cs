using Hardened.Commands;
using Hardened.Commands.Impl;
using Hardened.Shared.Runtime.Application;
using Hardened.Shared.Runtime.Attributes;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.Console.SUT;

[HardenedStartup]
public partial class Application {
    private IEnumerable<IApplicationModule> Modules() {
        yield return new CommandsLibrary();
    }
    
    /*
    private void RegisterDependencies(IServiceCollection serviceCollection) { 
        serviceCollection.AddTransient<ICommandDefinitionProvider, CommandDefinitionProvider>();
        serviceCollection.AddTransient<ICommandBinder<AddCommand>, AddBinder>();
    }
    
    private class AddBinder : ICommandBinder<AddCommand> {
        private ICommandLineArgumentConverter _converter;
        private IServiceProvider _serviceProvider;
        
        public AddBinder(ICommandLineArgumentConverter converter, IServiceProvider serviceProvider) {
            _converter = converter;
            _serviceProvider = serviceProvider;
        }

        public void Bind(IReadOnlyDictionary<string, string[]> data, AddCommand model) {
            model.X = _converter.Convert<int>("x", CommandOptionType.Number,
                data.GetValueOrDefault("x")?.FirstOrDefault(), default);
            
            model.Y = _converter.Convert<int>("y", CommandOptionType.Number,
                data.GetValueOrDefault("y")?.FirstOrDefault(), default);

            var binder = _serviceProvider.GetService<ICommandBinder<MathCommand>>();

            if (binder != null) {
                binder.Bind(data, model);
            }
        }
    }
    
    private class CommandDefinitionProvider : ICommandDefinitionProvider {
        
        public IEnumerable<CommandDefinition> ProvideDefinitions() {
            yield return 
                new CommandDefinition("", "add", null, "add command", new [] {
                    new CommandOption("x", CommandOptionType.Number, true, false),
                    new CommandOption("y", CommandOptionType.Number, true, false),
                }, Binder);
        }

        
        private Task<int> Binder(IServiceProvider serviceProvider, IReadOnlyDictionary<string, string[]> options) {
            var handler = serviceProvider.GetRequiredService<ICommandHandler<AddCommand>>();
            var binder = serviceProvider.GetRequiredService<ICommandBinder<AddCommand>>();

            var commandOptions = new AddCommand();
            
            binder.Bind(options, commandOptions);

            return handler.Handle(commandOptions);
        }
    }*/
}