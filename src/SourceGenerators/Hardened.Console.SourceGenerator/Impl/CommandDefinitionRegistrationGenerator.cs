using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.Console.SourceGenerator.Impl;

public class CommandDefinitionRegistrationGenerator {
    public void GenerateFile(
        SourceProductionContext sourceProductionContext,
        (EntryPointSelector.Model Left, ImmutableArray<CommandDefinitionModel> Right) commandData) {
        var commandsFile = new CSharpFileDefinition(commandData.Left.EntryPointType.Namespace);

        GeneratedCode(sourceProductionContext, commandData.Left, commandData.Right, commandsFile);

        var outputContext = new OutputContext();

        commandsFile.WriteOutput(outputContext);

        var fileName = commandData.Left.EntryPointType.Name + ".Commands.cs";

        sourceProductionContext.AddSource(fileName, outputContext.Output());
    }

    private void GeneratedCode(
        SourceProductionContext sourceProductionContext,
        EntryPointSelector.Model model,
        ImmutableArray<CommandDefinitionModel> commandDataRight,
        CSharpFileDefinition commandsFile) {
        var classDefinition = commandsFile.AddClass(model.EntryPointType.Name);

        classDefinition.Modifiers |= ComponentModifier.Partial;

        var commandRegistration =
            GenerateCommandRegistrationMethod(model, commandDataRight, classDefinition);
        classDefinition.AddUsingNamespace(
            KnownTypes.Namespace.Hardened.Shared.Runtime.Configuration);

        var templateField = classDefinition.AddField(typeof(int), "_commandsDi");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime
            .DependencyInjection);
        templateField.InitializeValue =
            $"DependencyRegistry<{classDefinition.Name}>.Register(RegisterCommands)";
    }

    private MethodDefinition GenerateCommandRegistrationMethod(
        EntryPointSelector.Model model,
        ImmutableArray<CommandDefinitionModel> commandDataRight,
        ClassDefinition classDefinition) {
        var registerMethod = classDefinition.AddMethod("RegisterCommands");

        registerMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var environment =
            registerMethod.AddParameter(KnownTypes.Application.IEnvironment, "environment");
        var serviceCollection =
            registerMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
        var entryPointDef = registerMethod.AddParameter(model.EntryPointType, "entryPoint");

        foreach (var commandDefinitionModel in commandDataRight) {
            GenerateCommandRegistrationCode(registerMethod, classDefinition, serviceCollection,
                commandDefinitionModel);
        }

        GenerateCommandDefinitionProvider(registerMethod, classDefinition, serviceCollection,
            commandDataRight);

        return registerMethod;
    }

    private void GenerateCommandDefinitionProvider(
        MethodDefinition registerMethod,
        ClassDefinition classDefinition,
        ParameterDefinition serviceCollection,
        ImmutableArray<CommandDefinitionModel> commandDataRight) {
        var commandDefinitionProvider = classDefinition.AddClass("CommandDefinitionProvider");

        var type = TypeDefinition.Get(KnownTypes.Namespace.Hardened.Commands.Impl,
            "ICommandDefinitionProvider");

        registerMethod.AddIndentedStatement(
            serviceCollection.Invoke(
                "AddTransient",
                TypeOf(type), 
                TypeOf(TypeDefinition.Get("", "CommandDefinitionProvider"))
                ));        
        
        commandDefinitionProvider.AddBaseType(type);

        var providerDefinitions = commandDefinitionProvider.AddMethod("ProvideDefinitions");

        providerDefinitions.SetReturnType(
            TypeDefinition.IEnumerable(
                TypeDefinition.Get(KnownTypes.Namespace.Hardened.Commands.Impl,
                    "CommandDefinition")));

        foreach (var commandDefinitionModel in commandDataRight) {
            var optionDefinitions = GetOptionDefinitions(commandDefinitionModel);

            var newStatement = New(TypeDefinition.Get(KnownTypes.Namespace.Hardened.Commands.Impl,
                    "CommandDefinition"),
                QuoteString(commandDefinitionModel.ParentName ?? ""),
                QuoteString(commandDefinitionModel.CommandName),
                QuoteString(""),
                QuoteString(commandDefinitionModel.Description ?? ""),
                optionDefinitions,
                $"CommandLineHelper<{commandDefinitionModel.CommandModelType.Name}>.Invoke"
            );

            var wrapper = new WrapStatement(newStatement, "yield return ", "");
            providerDefinitions.AddIndentedStatement(wrapper);
        }
    }

    private IOutputComponent GetOptionDefinitions(CommandDefinitionModel commandDefinitionModel) {
        var options = new List<IOutputComponent>();

        foreach (var optionModel in commandDefinitionModel.Options) {
            options.Add(
                New(
                    TypeDefinition.Get(KnownTypes.Namespace.Hardened.Commands.Impl,
                        "CommandOption"),
                    QuoteString(optionModel.OptionName),
                    "CommandOptionType." + optionModel.OptionType,
                    QuoteString(optionModel.Description),
                    optionModel.IsRequired.ToString().ToLower(),
                    optionModel.IsArray.ToString().ToLower()
                ));
        }

        return new WrapStatement(new ListOutputComponent(options), "new CommandOption[]{ ", " }");
    }

    private void GenerateCommandRegistrationCode(
        MethodDefinition registerMethod,
        ClassDefinition classDefinition,
        ParameterDefinition serviceCollection,
        CommandDefinitionModel commandDefinitionModel) {
        var binderType = new GenericTypeDefinition(
            TypeDefinitionEnum.InterfaceDefinition,
            KnownTypes.Namespace.Hardened.Commands.Impl,
            "ICommandBinder",
            new[] { commandDefinitionModel.CommandModelType }
        );

        var binderClass = GenerateBinderCode(registerMethod, classDefinition, serviceCollection,
            commandDefinitionModel, binderType);

        registerMethod.AddIndentedStatement(serviceCollection.Invoke("AddTransient",
            TypeOf(binderType),
            TypeOf(
                TypeDefinition.Get("", binderClass.Name))
        ));
    }

    private ClassDefinition GenerateBinderCode(
        MethodDefinition registerMethod,
        ClassDefinition classDefinition,
        ParameterDefinition serviceCollection,
        CommandDefinitionModel commandDefinitionModel,
        ITypeDefinition binderType) {
        var className = commandDefinitionModel.CommandModelType.Name;

        if (commandDefinitionModel.ParentType != null) {
            className = $"{commandDefinitionModel.ParentType.Name}_{className}";
        }

        var binderClass =
            classDefinition.AddClass(className + "Binder");

        binderClass.AddBaseType(binderType);

        var converterType = TypeDefinition.Get(KnownTypes.Namespace.Hardened.Commands.Impl,
            "ICommandLineArgumentConverter");

        var fields = binderClass.AddSimpleConstructor(
            new ClassDefinitionExtensions.ConstructorParameter(
                KnownTypes.DI.IServiceProvider, "serviceProvider"),
            new ClassDefinitionExtensions.ConstructorParameter(converterType, "converterType"));

        GenerateBindMethod(binderClass, fields, commandDefinitionModel);

        return binderClass;
    }

    private void GenerateBindMethod(
        ClassDefinition binderClass,
        List<FieldDefinition> fields,
        CommandDefinitionModel commandDefinitionModel) {
        var bindMethod = binderClass.AddMethod("Bind");
        var converter = fields.First(f => f.Name == "_converterType");
        var serviceProvider = fields.First(f => f.Name == "_serviceProvider");

        var dataParameter = bindMethod.AddParameter(
            new GenericTypeDefinition(typeof(IReadOnlyDictionary<,>),
                new[] { TypeDefinition.Get(typeof(string)), TypeDefinition.Get(typeof(string[])) }),
            "data"
        );

        var modelParameter = bindMethod.AddParameter(
            commandDefinitionModel.CommandModelType,
            "model"
        );

        foreach (var optionModel in commandDefinitionModel.Options) {
            var dataValueStatement = SyntaxHelpers.Question(
                dataParameter.Invoke("GetValueOrDefault",
                    SyntaxHelpers.QuoteString(optionModel.OptionName))
            ).Invoke("FirstOrDefault");

            var convert = converter.Instance.InvokeGeneric("Convert",
                new[] { optionModel.PropertyType },
                SyntaxHelpers.QuoteString(optionModel.OptionName),
                "CommandOptionType." + optionModel.OptionType,
                dataValueStatement,
                string.IsNullOrEmpty(optionModel.DefaultValue)
                    ? "default"
                    : optionModel.DefaultValue
            );

            bindMethod.Assign(convert).To(modelParameter.Property(optionModel.PropertyName));
        }

        if (commandDefinitionModel.ParentType != null) {
            var parentBinder = new GenericTypeDefinition(TypeDefinitionEnum.ClassDefinition,
                KnownTypes.Namespace.Hardened.Commands.Impl,
                "ICommandBinder",
                new[] { commandDefinitionModel.ParentType }
            );

            var binder =
                bindMethod
                    .Assign(serviceProvider.Instance.InvokeGeneric("GetService",
                        new[] { parentBinder })).ToVar("binder");

            var ifStatement =
                bindMethod.If(SyntaxHelpers.NotEquals(binder, SyntaxHelpers.Null()));

            ifStatement.AddIndentedStatement(binder.Invoke("Bind", dataParameter, modelParameter));
        }
    }
}