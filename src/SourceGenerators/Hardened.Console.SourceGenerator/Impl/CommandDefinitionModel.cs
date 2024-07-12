using CSharpAuthor;

namespace Hardened.Console.SourceGenerator.Impl;

public record CommandOptionModel(
    string OptionName,
    string PropertyName,
    ITypeDefinition PropertyType,
    string OptionType,
    string Description,
    string DefaultValue,
    bool IsArray,
    bool IsRequired);

public record CommandDefinitionModel(
    ITypeDefinition CommandModelType,
    ITypeDefinition? ParentType,
    string CommandName,
    string? ParentName,
    string? Description,
    IReadOnlyList<CommandOptionModel> Options) {
    
    public override int GetHashCode() {
        int code = CommandModelType.GetHashCode() * 23;

        if (ParentType != null) {
            code *= ParentType.GetHashCode();
        }

        code *= CommandName.GetHashCode();

        if (ParentName != null) {
            code *= ParentName.GetHashCode();
        }

        if (Description != null) {
            code *= Description.GetHashCode();
        }
        
        return code;
    }

    public virtual bool Equals(CommandDefinitionModel? other) {
        if (other == null) {
            return false;
        }

        if (!CommandModelType.Equals(other.CommandModelType)) {
            return false;
        }

        if (ParentType == null && other.ParentType != null) {
            return false;
        }

        if (!(ParentType?.Equals(other.ParentType) ?? true)) {
            return false;
        }
        
        if (Options.Count != other.Options.Count) {
            return false;
        }

        if (CommandName != other.CommandName) {
            return false;
        }

        if (ParentName != other.ParentName) {
            return false;
        }

        if (Description != other.Description) {
            return false;
        }
        
        for (var i = 0; i < Options.Count; i++) {
            if (!Options[i].Equals(other.Options[i])) {
                return false;
            }
        }
        
        return true;
    }
}