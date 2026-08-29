using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.DependencyInjection;

public class ServiceProviderFileGenerator {
    public void GenerateFile(
        SourceProductionContext sourceProductionContext,
        EntryPointSelector.Model model) {
        var diFile = new CSharpFileDefinition(model.EntryPointType.Namespace);

        // AddLogging, BuildServiceProvider and GetRequiredService are extension methods, and an
        // extension method is reachable only through a using of its namespace - global:: cannot
        // name one.
        diFile.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        GenerateCode(model, diFile);

        var outputContext = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        diFile.WriteOutput(outputContext);

        var fileName = model.EntryPointType.Name + ".ServiceProvider.cs";

        sourceProductionContext.AddSource(fileName, GeneratedSource.Header(outputContext.Output()));
    }

    private void GenerateCode(EntryPointSelector.Model model, CSharpFileDefinition diFile) {
        var applicationDefinition = diFile.AddClass(model.EntryPointType.Name);

        applicationDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        GenerateCreateServiceProvider(model, applicationDefinition);
    }

    private void GenerateCreateServiceProvider(
        EntryPointSelector.Model model,
        ClassDefinition applicationDefinition) {
        var providerMethod = applicationDefinition.AddMethod("CreateServiceProvider");
        providerMethod.Modifiers = ComponentModifier.Public;

        providerMethod.SetReturnType(KnownTypes.DI.ServiceProvider);

        var environment =
            providerMethod.AddParameter(KnownTypes.Application.IHardenedEnvironment, "environment");

        var overrideDependenciesDefinition = providerMethod.AddParameter(
            TypeDefinition
                .Action(KnownTypes.Application.IHardenedEnvironment, KnownTypes.DI.IServiceCollection)
                .MakeNullable(),
            "overrideDependencies");

        ParameterDefinition loggingBuilderAction
            = providerMethod.AddParameter(
                TypeDefinition.Action(KnownTypes.Logging.ILoggingBuilder).MakeNullable(),
                "loggingBuilderAction");

        var initAction = providerMethod.AddParameter(
            TypeDefinition
                .Action(KnownTypes.Application.IHardenedEnvironment, KnownTypes.DI.IServiceCollection)
                .MakeNullable(),
            "initDependencies");

        initAction.DefaultValue = Null();

        providerMethod.AddUsingNamespace("Microsoft.Extensions.DependencyInjection.Extensions");

        var serviceCollectionDefinition =
            providerMethod.Assign(New(KnownTypes.DI.ServiceCollection)).ToVar("serviceCollection");

        providerMethod.NewLine();

        var loggerStatement = NullCoalesce(loggingBuilderAction, "(b => {})");

        loggerStatement.PrintParentheses = false;
        loggerStatement.Indented = false;

        providerMethod.AddIndentedStatement(
            serviceCollectionDefinition.Invoke(
                "AddLogging", loggerStatement));

        providerMethod.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime.Logging);

        // AddHardenedEnvironment rather than AddSingleton, because the environment has to be
        // reachable as IModuleEnvironment as well as IHardenedEnvironment. AddSingleton registers
        // the parameter's static type only, so [IfEnvironment] found nothing here and fell back to
        // ASPNETCORE_ENVIRONMENT - Production - while the rest of the application read
        // HARDENED_ENVIRONMENT and said development. This method builds the collection for every
        // host that has no Program.cs of its own, Lambda among them.
        providerMethod.AddUsingNamespace(KnownTypes.Namespace.Hardened.Shared.Runtime.Application);

        providerMethod.AddIndentedStatement(
            serviceCollectionDefinition.Invoke("AddHardenedEnvironment", "environment")
        );

        providerMethod.NewLine();

        providerMethod.AddIndentedStatement(
            "initDependencies?.Invoke(environment, serviceCollection)");

        providerMethod.NewLine();

        providerMethod.AddIndentedStatement(
            new CodeOutputComponent("this.PopulateServiceCollection(serviceCollection)") {
                Indented = false
            });

        providerMethod.AddIndentedStatement(
            "overrideDependencies?.Invoke(environment, serviceCollection)");

        providerMethod.NewLine();

        providerMethod.Return(serviceCollectionDefinition.Invoke("BuildServiceProvider"));
    }
}
