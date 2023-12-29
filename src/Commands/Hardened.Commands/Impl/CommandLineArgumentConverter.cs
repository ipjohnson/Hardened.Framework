using Hardened.Shared.Runtime.Attributes;
using Hardened.Shared.Runtime.Json;

namespace Hardened.Commands.Impl;

public interface ICommandLineArgumentConverter {
    T Convert<T>(string optionName, CommandOptionType optionType, string? argument, T? defaultValue);
}

[Expose]
public class CommandLineArgumentConverter : ICommandLineArgumentConverter {
    private readonly IJsonSerializer _jsonSerializer;
    
    public CommandLineArgumentConverter(IJsonSerializer jsonSerializer) {
        _jsonSerializer = jsonSerializer;
    }

    public T Convert<T>(string optionName, CommandOptionType optionType, string? argument, T? defaultValue) {
        if (argument == null) {
            if (defaultValue != null) {
                return defaultValue;
            }
            
            throw new Exception("Argument should not be null at this point");
        }
        
        if (typeof(T) == typeof(string)) {
            return (T)(object)argument;
        }

        switch (optionType) {
            case CommandOptionType.String:
                return _jsonSerializer.Deserialize<T>(argument);
            
            case CommandOptionType.File:
                if (!File.Exists(argument)) {
                    throw new Exception($"File {argument} does not exist for option {optionName}");
                }
                
                return _jsonSerializer.Deserialize<T>(argument);
            
            case CommandOptionType.Boolean:
                case CommandOptionType.Number:
   
                    return (T)System.Convert.ChangeType(argument, typeof(T));
            default:
                throw new Exception("Unknown option type: " + optionType);
        }
    }
}