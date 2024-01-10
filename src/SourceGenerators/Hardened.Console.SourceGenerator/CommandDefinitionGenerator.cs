using CSharpAuthor;
using Hardened.Console.SourceGenerator.Impl;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Collections.Immutable;

namespace Hardened.Console.SourceGenerator;

public class CommandDefinitionGenerator {
    public static void Setup(IncrementalGeneratorInitializationContext context, IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider) {
        var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.Commands.CommandAttribute);

        var services = context.SyntaxProvider.CreateSyntaxProvider(
            classSelector.Where,
            GenerateCommandDefinitionModel
        ).WithComparer(new CommandDefinitionModelComparer());

        var servicesCollection = services.Collect();

        var generator = new CommandDefinitionRegistrationGenerator();

        context.RegisterSourceOutput(
            entryPointProvider.Combine(servicesCollection),
            SourceGeneratorWrapper.Wrap<
                (EntryPointSelector.Model Left, ImmutableArray<CommandDefinitionModel> Right)>(generator.GenerateFile));
    }

    private static CommandDefinitionModel GenerateCommandDefinitionModel(GeneratorSyntaxContext context, CancellationToken cancellationToken) {
        var syntax = (ClassDeclarationSyntax)context.Node;
        var commandName = "";
        string? parentName = null;
        string? description = null;
        
        var commandAttribute = syntax.GetAttribute("Command", KnownTypes.Namespace.Hardened.Commands.Attributes);

        if (commandAttribute?.ArgumentList != null) {
            foreach (var attributeArgumentSyntax in commandAttribute.ArgumentList.Arguments) {
                var nodes = attributeArgumentSyntax.ChildNodes().ToArray();
                if (nodes.Length == 1) {
                    commandName = nodes[0].ToString().Trim('"');
                } else if (nodes.Length == 2) {
                    switch (nodes[0].ToString().Trim('=', ' ')) {
                        case "Description":
                            description = nodes[1].ToString().Trim('"');
                            break;
                        case "ParentCommand":
                            parentName = nodes[1].ToString().Trim('"');
                            break;
                    }
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        
        var options = GetOptions(syntax, context, cancellationToken);

        ITypeDefinition? parentType = null;

        if (syntax.BaseList != null) {
            foreach (var childNode in syntax.BaseList.ChildNodes()) {
                var child = childNode.DescendantNodes().OfType<IdentifierNameSyntax>().FirstOrDefault();
                parentType = child?.GetTypeDefinition(context);
            }
        }
        
        var definition = new CommandDefinitionModel(
            syntax.GetTypeDefinition(), 
            parentType,
            commandName, 
            parentName, 
            description, 
            options);

        return definition;
    }

    private static IReadOnlyList<CommandOptionModel> GetOptions(ClassDeclarationSyntax syntax, GeneratorSyntaxContext context, CancellationToken cancellationToken) { 
        var options = new List<CommandOptionModel>();

        foreach (var syntaxMember in syntax.Members) {
            if (syntaxMember is PropertyDeclarationSyntax propertyDeclarationSyntax) {
                var nameAndDescription = GetNameAndDescription(propertyDeclarationSyntax);
                var defaultAndRequired = GetDefaultAndRequired(propertyDeclarationSyntax);
                var optionTypeAndArray = GetOptionTypeAndArray(propertyDeclarationSyntax, context);
                var type = propertyDeclarationSyntax.Type.GetTypeDefinition(context);
                
                options.Add(new CommandOptionModel(
                    nameAndDescription.Item1,
                    propertyDeclarationSyntax.Identifier.ToString(),
                    type!,
                    optionTypeAndArray.Item1,
                    nameAndDescription.Item2,
                    defaultAndRequired.Item1,
                    optionTypeAndArray.Item2,
                    defaultAndRequired.Item2
                    ));
            }
        }
        
        return options;
    }

    private static Tuple<string, bool> GetOptionTypeAndArray(
        PropertyDeclarationSyntax propertyDeclarationSyntax,
        GeneratorSyntaxContext context) {
        return new Tuple<string, bool>("String", false);
    }

    private static Tuple<string, bool> GetDefaultAndRequired(PropertyDeclarationSyntax propertyDeclarationSyntax) {
        return new Tuple<string, bool>("", true);
    }

    private static Tuple<string,string> GetNameAndDescription(PropertyDeclarationSyntax propertyDeclarationSyntax) {
        return new System.Tuple<string, string>(
            propertyDeclarationSyntax.Identifier.ToString().ToLower(), "");
    }
}