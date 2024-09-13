using System.Collections;

namespace Hardened.Console.SourceGenerator.Impl;

public class CommandDefinitionModelComparer : IEqualityComparer<CommandDefinitionModel> {
    public bool Equals(CommandDefinitionModel? x, CommandDefinitionModel? y) {
        if (x is null && y is null) {
            return true;
        }

        if (x is null || y is null) {
            return false;
        }
        
        return x.Equals(y);
    }

    public int GetHashCode(CommandDefinitionModel obj) {
        return obj.GetHashCode();
    }
}