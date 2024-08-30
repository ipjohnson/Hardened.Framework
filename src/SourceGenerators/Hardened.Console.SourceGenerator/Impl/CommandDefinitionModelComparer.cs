using System.Collections;

namespace Hardened.Console.SourceGenerator.Impl;

public class CommandDefinitionModelComparer : IEqualityComparer<CommandDefinitionModel> {
    public bool Equals(CommandDefinitionModel? x, CommandDefinitionModel? y) {
        return x.Equals(y);
    }

    public int GetHashCode(CommandDefinitionModel obj) {
        return obj.GetHashCode();
    }
}