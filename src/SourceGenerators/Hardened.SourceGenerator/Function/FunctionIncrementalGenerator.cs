using System.Collections.Immutable;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Function;

public static class FunctionIncrementalGenerator {
    public static void Setup(
        IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider) {
        var methodSelector =
            new SyntaxSelector<MethodDeclarationSyntax>(KnownTypes.Requests.HardenedFunctionAttribute);
        var modelGenerator = new FunctionModelGenerator();

        // See WebIncrementalGenerator: validation builds the model and attaches its own filter, so
        // a [HardenedFunction] whose payload type carries constraints validates without the author
        // writing anything.
        var modelProvider = HandlerValidationGenerator.Setup(
            initializationContext, modelGenerator, methodSelector.Where);

        // Invoker stage - generate invoker classes (one per handler)
        initializationContext.RegisterSourceOutput(
            modelProvider,
            SourceGeneratorWrapper.Wrap<RequestHandlerModel>(GenerateInvokerSource)
        );

        // Registration stage - generate FunctionHandlerProvider + DI
        var collection = modelProvider.Collect();

        var combined = entryPointProvider.Combine(collection).WithComparer(new CombinedComparer());
        initializationContext.RegisterSourceOutput(combined,
            SourceGeneratorWrapper.Wrap<
                (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right)>(
                GenerateFunctionHandlerProvider));
    }

    private static void GenerateInvokerSource(SourceProductionContext context, RequestHandlerModel model) {
        context.CancellationToken.ThrowIfCancellationRequested();

        var csharpFile = new CSharpFileDefinition(model.InvokeHandlerType.Namespace);

        InvokeClassGenerator.GenerateInvokeClass(model, csharpFile, context.CancellationToken);

        var outputContext = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        csharpFile.WriteOutput(outputContext);

        context.AddSource(model.Name.Path + ".FunctionHandler.cs", GeneratedSource.Header(outputContext.Output()));
    }

    private static void GenerateFunctionHandlerProvider(SourceProductionContext context,
        (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) models) {
        context.CancellationToken.ThrowIfCancellationRequested();

        var appModel = models.Left;
        var requestHandlers = models.Right;

        var csharpFile = new CSharpFileDefinition(appModel.EntryPointType.Namespace);

        // AddSingleton/AddTransient are extension methods, and an extension method is reachable
        // only through a using of its namespace - global:: cannot name one.
        csharpFile.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        var appClass = csharpFile.AddClass(appModel.EntryPointType.Name);
        appClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        CreateFunctionHandlerProviderClass(requestHandlers, appClass, context.CancellationToken);
        SetupDiForFunctionHandlers(
            requestHandlers, appClass, appModel.EntryPointType.Namespace, context.CancellationToken);

        var output = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        csharpFile.WriteOutput(output);

        context.AddSource(appModel.EntryPointType.Name + ".FunctionHandlers.cs", GeneratedSource.Header(output.Output()));
    }

    private static void CreateFunctionHandlerProviderClass(
        ImmutableArray<RequestHandlerModel> requestHandlers, ClassDefinition appClass,
        CancellationToken cancellationToken) {
        var providerClass = appClass.AddClass("FunctionHandlerProvider");
        providerClass.Modifiers = ComponentModifier.Private;
        providerClass.AddBaseType(KnownTypes.Requests.IFunctionHandlerProvider);

        // Constructor with IServiceProvider
        var field = providerClass.AddField(typeof(IServiceProvider), "_serviceProvider");

        var constructor = providerClass.AddConstructor();
        var spParam = constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        constructor.Assign(spParam).To(field.Instance);

        // GetFunctionHandler method
        var method = providerClass.AddMethod("GetFunctionHandler");
        method.SetReturnType(KnownTypes.Requests.IExecutionRequestHandler.MakeNullable());
        var functionNameParam = method.AddParameter(typeof(string), "functionName");
        var serviceProviderParam = method.AddParameter(KnownTypes.DI.IServiceProvider, "serviceProvider");

        if (requestHandlers.Length > 0) {
            // Handlers with explicit function names go in the switch.
            // Handlers without explicit names (Name.Path == HandlerMethod) are catch-all.
            var namedHandlers = requestHandlers.Where(h => h.Name.Path != h.HandlerMethod).ToList();
            var defaultHandlers = requestHandlers.Where(h => h.Name.Path == h.HandlerMethod).ToList();

            if (namedHandlers.Count > 0) {
                var switchBlock = method.Switch(functionNameParam);

                foreach (var handler in namedHandlers) {
                    cancellationToken.ThrowIfCancellationRequested();

                    var caseBlock = switchBlock.AddCase($"\"{handler.Name.Path}\"");
                    caseBlock.Return(New(handler.InvokeHandlerType, serviceProviderParam));
                }
            }

            // Unnamed handlers match any function name (catch-all)
            if (defaultHandlers.Count > 0) {
                method.Return(New(defaultHandlers[0].InvokeHandlerType, serviceProviderParam));
                return;
            }
        }

        method.Return(new CodeOutputComponent("null"));
    }

    private static void SetupDiForFunctionHandlers(
        ImmutableArray<RequestHandlerModel> requestHandlers, ClassDefinition appClass,
        string appNamespace, CancellationToken cancellationToken) {
        var templateField = appClass.AddField(typeof(int), "_functionHandlersDi");

        templateField.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;
        templateField.AddUsingNamespace(KnownTypes.Namespace.DependencyModules.Runtime.Helpers);
        templateField.InitializeValue =
            new CodeOutputComponent($"DependencyRegistry<{appClass.Name}>.Add(FunctionHandlersDI)");
        templateField.AddAttribute(
            TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "DynamicDependency"),
            "nameof(FunctionHandlersDI)");

        var diMethod = appClass.AddMethod("FunctionHandlersDI");
        diMethod.Modifiers |= ComponentModifier.Static | ComponentModifier.Private;

        var serviceCollection = diMethod.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

        diMethod.AddIndentedStatement(serviceCollection.InvokeGeneric("AddSingleton",
            new[] {
                KnownTypes.Requests.IFunctionHandlerProvider,
                // The nested provider by its full name: an empty-namespace TypeDefinition now
                // means the global namespace, which Global mode qualifies - and
                // global::FunctionHandlerProvider names nothing.
                TypeDefinition.Get(appNamespace, appClass.Name + ".FunctionHandlerProvider")
            }));

        var handlerTypes = requestHandlers.Select(m => m.ControllerType).Distinct();

        foreach (var handlerType in handlerTypes) {
            cancellationToken.ThrowIfCancellationRequested();

            diMethod.AddIndentedStatement(
                serviceCollection.InvokeGeneric("AddTransient", new[] { handlerType }));
        }

        Validation.ParameterValidatorRegistration.Write(
            diMethod, serviceCollection, requestHandlers, cancellationToken);
    }

    public class CombinedComparer : IEqualityComparer<(EntryPointSelector.Model Left,
        ImmutableArray<RequestHandlerModel> Right)> {
        public bool Equals((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) x,
            (EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) y) {
            return x.Item1.Equals(y.Item1) && ((Object)x.Item2).Equals(y.Item2);
        }

        public int GetHashCode((EntryPointSelector.Model Left, ImmutableArray<RequestHandlerModel> Right) obj) {
            unchecked {
                return (obj.Item1.GetHashCode() * 397) ^ obj.Item2.GetHashCodeAggregation();
            }
        }
    }
}
