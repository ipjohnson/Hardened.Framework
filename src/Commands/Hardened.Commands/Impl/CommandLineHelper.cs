using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Commands.Impl;

public class CommandLineHelper<T> where T : new(){
    public static Task<int> Invoke(
        IServiceProvider serviceProvider,
        IReadOnlyDictionary<string, string[]> options) {
        var handler = serviceProvider.GetRequiredService<ICommandHandler<T>>();
        var binder = serviceProvider.GetRequiredService<ICommandBinder<T>>();

        var commandOptions = new T();

        binder.Bind(options, commandOptions);

        return handler.Handle(commandOptions);
    }
}