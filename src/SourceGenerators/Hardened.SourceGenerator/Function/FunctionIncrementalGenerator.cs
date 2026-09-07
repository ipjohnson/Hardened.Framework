using System.Collections.Immutable;
using System.Threading;
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
        // [HardenedFunction] plus every trigger attribute. A trigger is a handler declaration as
        // much as [HardenedFunction] is - it names a route and a scheme - so it goes through the
        // same model, invoker and registration as the rest rather than a parallel pipeline.
        var selectors = TriggerModuleGenerator.Triggers
            .Select(trigger => new SyntaxSelector<MethodDeclarationSyntax>(trigger.Type))
            .ToArray();

        bool MethodSelector(SyntaxNode node, CancellationToken token) =>
            selectors.Any(selector => selector.Where(node, token));
        var modelGenerator = new FunctionModelGenerator();

        // See WebIncrementalGenerator: validation builds the model and attaches its own filter, so
        // a [HardenedFunction] whose payload type carries constraints validates without the author
        // writing anything.
        var modelProvider = HandlerValidationGenerator.Setup(
            initializationContext, modelGenerator, MethodSelector);

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

        // Trimmed, not rewritten. A trigger routes under a rooted path - "/orders-new" - and a
        // hint name may not begin with a separator, so the generator threw and emitted nothing for
        // every trigger handler. An interior slash is legal and is kept: [HardenedFunction] has
        // always put the function name in the file name verbatim, "orders/received" included.
        context.AddSource(
            model.Name.Path.Trim('/') + ".FunctionHandler.cs",
            GeneratedSource.Header(outputContext.Output()));
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
            // Handlers without explicit names are catch-all: a Lambda hosting one operation never
            // sends a name worth matching. The route is rooted and the method name is not, so the
            // comparison trims - it used to compare two bare names, and prepending the root to
            // every route made every unnamed handler look named.
            bool Named(RequestHandlerModel handler) =>
                handler.Name.Path.Trim('/') != handler.HandlerMethod;

            var namedHandlers = requestHandlers.Where(Named).ToList();
            var defaultHandlers = requestHandlers.Where(handler => !Named(handler)).ToList();

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
