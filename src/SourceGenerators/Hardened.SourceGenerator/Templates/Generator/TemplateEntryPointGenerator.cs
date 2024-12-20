﻿using System.Collections.Immutable;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Templates.Generator;

public static class TemplateEntryPointGenerator {
    public static void Generate(SourceProductionContext productionContext,
        (EntryPointSelector.Model applicationModel, ImmutableArray<TemplateIncrementalGenerator.TemplateModel>
            templateModels) templateData) {
        var applicationFile = new CSharpFileDefinition(templateData.applicationModel.EntryPointType.Namespace);

        var classDefinition = applicationFile.AddClass(templateData.applicationModel.EntryPointType.Name);

        classDefinition.Modifiers |= ComponentModifier.Partial | ComponentModifier.Public;

        var templateProviderClass =
            CreateTemplateProviderClass(classDefinition, templateData.applicationModel, templateData.templateModels);

        GenerateDependencyInjection(classDefinition, templateProviderClass, templateData.applicationModel,
            templateData.templateModels);

        var templateFileName = templateData.applicationModel.EntryPointType.Name + ".Templates.cs";

        var outputContext = new OutputContext();

        applicationFile.WriteOutput(outputContext);

        productionContext.AddSource(templateFileName, outputContext.Output());
    }

    private static void GenerateDependencyInjection(ClassDefinition classDefinition,
        ITypeDefinition templateProviderClass, EntryPointSelector.Model applicationModel,
        ImmutableArray<TemplateIncrementalGenerator.TemplateModel> templateModels) {
        var templateField = classDefinition.AddField(typeof(int), "_templateDependencies");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime.DependencyInjection);
        templateField.InitializeValue = $"DependencyRegistry<{classDefinition.Name}>.Register(HardenedTemplateDI)";

        var diMethod = classDefinition.AddMethod("HardenedTemplateDI");

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var environment = diMethod.AddParameter(KnownTypes.Application.IHardenedEnvironment, "environment");
        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
        var entryPoint = diMethod.AddParameter(applicationModel.EntryPointType, "entryPoint");

        diMethod.AddIndentedStatement(Invoke(
            KnownTypes.DI.Registry.TemplateDI, "Register", environment, serviceCollection));

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] { KnownTypes.Templates.ITemplateExecutionHandlerProvider, templateProviderClass }));
    }

    private static ITypeDefinition CreateTemplateProviderClass(ClassDefinition classDefinition,
        EntryPointSelector.Model applicationModel,
        ImmutableArray<TemplateIncrementalGenerator.TemplateModel> templateModels) {
        var templateProviderClass = classDefinition.AddClass("TemplateProvider");

        templateProviderClass.AddBaseType(KnownTypes.Templates.ITemplateExecutionHandlerProvider);

        GenerateConstructor(templateProviderClass);

        GenerateProviderMethod(classDefinition, templateProviderClass, applicationModel, templateModels);

        return TypeDefinition.Get(applicationModel.EntryPointType.Namespace,
            applicationModel.EntryPointType.Name + "." + "TemplateProvider");
    }

    private static void GenerateConstructor(ClassDefinition templateProviderClass) {
        var templateServices = templateProviderClass.AddField(KnownTypes.Templates.IInternalTemplateServices,
            "_internalTemplateServices");

        var constructor = templateProviderClass.AddConstructor();

        var parameter =
            constructor.AddParameter(KnownTypes.Templates.IInternalTemplateServices, "internalTemplateServices");

        constructor.Assign(parameter).To(templateServices.Instance);
    }

    private static void GenerateProviderMethod(
        ClassDefinition classDefinition,
        ClassDefinition templateProviderClass,
        EntryPointSelector.Model applicationModel,
        ImmutableArray<TemplateIncrementalGenerator.TemplateModel> templateModels) {
        var templateExecutionService = templateProviderClass.AddProperty(
            KnownTypes.Templates.ITemplateExecutionService.MakeNullable(), "TemplateExecutionService");

        var handlerMethod = templateProviderClass.AddMethod("GetTemplateExecutionHandler");

        handlerMethod.SetReturnType(KnownTypes.Templates.ITemplateExecutionHandler.MakeNullable());

        var templateNameParameter = handlerMethod.AddParameter(typeof(string), "templateName");

        GenerateProviderMethodBody(
            applicationModel, templateModels, templateProviderClass, handlerMethod, templateNameParameter,
            templateExecutionService);
    }

    private static void GenerateProviderMethodBody(EntryPointSelector.Model applicationModel,
        ImmutableArray<TemplateIncrementalGenerator.TemplateModel> templateModels,
        ClassDefinition templateProviderClass,
        MethodDefinition handlerMethod,
        ParameterDefinition templateNameParameter,
        PropertyDefinition templateExecutionService) {
        var internalServices = templateProviderClass.Fields.First(f => f.Name == "_internalTemplateServices");

        var switchBlock = handlerMethod.Switch(templateNameParameter);

        foreach (var templateModel in
                 templateModels.Sort((x, y) => string.CompareOrdinal(x.TemplateName, y.TemplateName))) {
            var instanceField = templateProviderClass.AddField(
                KnownTypes.Templates.ITemplateExecutionHandler.MakeNullable(),
                "_instance_" + SanitizeNameString(templateModel.TemplateName));

            var caseBlock = switchBlock.AddCase(QuoteString(templateModel.TemplateName));

            caseBlock.Return(NullCoalesceEqual(instanceField.Instance,
                New(templateModel.TemplateDefinitionType!, templateExecutionService.Instance,
                    internalServices.Instance)));
        }

        handlerMethod.Return(Null());
    }

    private static string SanitizeNameString(string name) {
        return name.Replace(".", "_").Replace("-", "_");
    }
}