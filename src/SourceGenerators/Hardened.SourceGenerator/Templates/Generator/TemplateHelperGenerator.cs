﻿using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.DependencyInjection;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Templates.Generator;

public static class TemplateHelperGenerator {
    public static void Generate(
        SourceProductionContext context,
        (EntryPointSelector.Model applicationModel, ImmutableArray<TemplateIncrementalGenerator.TemplateHelperModel>
            helperModels) helperData) {
        if (!helperData.helperModels.Any()) {
            return;
        }

        var helperFile = new CSharpFileDefinition(helperData.applicationModel.EntryPointType.Namespace);

        var appClass = helperFile.AddClass(helperData.applicationModel.EntryPointType.Name);

        appClass.Modifiers = ComponentModifier.Partial | ComponentModifier.Public;

        CreateHelperProviderClass(helperData, appClass);

        SetupDependencyInjection(helperData.applicationModel.EntryPointType, appClass, helperData.helperModels);

        var fileName = helperData.applicationModel.EntryPointType.Name + ".TemplateHelpers.cs";

        var outputContext = new OutputContext();

        helperFile.WriteOutput(outputContext);

        context.AddSource(fileName, outputContext.Output());
    }

    private static void SetupDependencyInjection(ITypeDefinition applicationModel, ClassDefinition appClass,
        ImmutableArray<TemplateIncrementalGenerator.TemplateHelperModel> helperModels) {
        var templateField = appClass.AddField(typeof(int), "_templateHelperDependencies");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime.DependencyInjection);
        templateField.InitializeValue = $"DependencyRegistry<{appClass.Name}>.Register(HardenedTemplateHelperDI)";

        var diMethod = appClass.AddMethod("HardenedTemplateHelperDI");

        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var environment = diMethod.AddParameter(KnownTypes.Application.IHardenedEnvironment, "environment");
        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");
        var entryPoint = diMethod.AddParameter(applicationModel, "entryPoint");

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] {
                KnownTypes.Templates.ITemplateHelperProvider,
                TypeDefinition.Get(applicationModel.Namespace, applicationModel.Name + ".TemplateHelperProvider")
            }));

        foreach (var helperModel in helperModels) {
            string methodName;

            switch (helperModel.Lifestyle) {
                case DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Singleton:
                    methodName = "AddSingleton";
                    break;
                case DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Scoped:
                    methodName = "AddScoped";
                    break;
                default:
                    methodName = "AddTransient";
                    break;
            }

            diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric(methodName,
                new[] { helperModel.Helper }));
        }
    }

    private static void CreateHelperProviderClass(
        (EntryPointSelector.Model applicationModel, ImmutableArray<TemplateIncrementalGenerator.TemplateHelperModel>
            helperModels) helperData,
        ClassDefinition appClass) {
        var templateHelperProvider = appClass.AddClass("TemplateHelperProvider");

        templateHelperProvider.AddBaseType(KnownTypes.Templates.ITemplateHelperProvider);

        var providerMethod = templateHelperProvider.AddMethod("GetTemplateHelperFactory");
        providerMethod.SetReturnType(KnownTypes.Templates.TemplateHelperFactory);

        var mustacheToken = providerMethod.AddParameter(typeof(string), "mustacheToken");

        var switchBlock = providerMethod.Switch(mustacheToken);

        foreach (var helperDataHelperModel in helperData.helperModels) {
            var helperField = templateHelperProvider.AddField(KnownTypes.Templates.TemplateHelperFactory,
                "_" + helperDataHelperModel.Name + "Field");

            var caseBlock = switchBlock.AddCase(QuoteString(helperDataHelperModel.Name));

            caseBlock.AddUsingNamespace(helperDataHelperModel.Helper.Namespace);

            caseBlock.Return(
                NullCoalesceEqual(
                    helperField.Instance,
                    "provider => provider.GetRequiredService<" + helperDataHelperModel.Helper.Name + ">()"));
        }

        providerMethod.Return(Null());
    }
}