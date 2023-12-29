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
    IReadOnlyList<CommandOptionModel> Options);