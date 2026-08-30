using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

public class LambdaApplicationEntryPointWriter : ApplicationEntryPointFileWriter {
    protected override void CustomConstructorLogic(EntryPointSelector.Model entryPoint, ClassDefinition appClass,
        ConstructorDefinition constructor,
        ParameterDefinition environment) {
        var providerInstanceDefinition =
            appClass.Fields.First(f => f.Name == RootServiceProvider).Instance;

        var filterProvider =
            constructor.Assign(RequiredModuleService.Resolve(
                KnownTypes.Lambda.ILambdaInvokeFilterProvider,
                RootServiceProvider,
                entryPoint.EntryPointType.Name,
                "LambdaFunctionModule")).ToVar("filterProvider");

        constructor.Assign(filterProvider.Invoke("ProvideFilter", RootServiceProvider)).ToVar("handler");

        // GetRequiredService is an extension method, and an extension method is reachable only
        // through a using of its namespace - global:: cannot name one. CSharpAuthor 2.0 derives
        // the using list from the types actually written, so nothing supplies this for us.
        var resolveMiddleware = providerInstanceDefinition.InvokeGeneric("GetRequiredService",
            new[] { KnownTypes.Requests.IMiddlewareService });

        resolveMiddleware.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        var middleware = constructor.Assign(resolveMiddleware).ToVar("middleware");

        constructor.AddIndentedStatement(middleware.Invoke("Use", "_ => handler"));

        var lambdaFunctionImplField = appClass.AddField(KnownTypes.Lambda.ILambdaFunctionImplService,
            "_lambdaFunctionImplService");

        var resolveImpl = providerInstanceDefinition.InvokeGeneric("GetRequiredService",
            new[] { KnownTypes.Lambda.ILambdaFunctionImplService });

        resolveImpl.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        constructor.Assign(resolveImpl).To(lambdaFunctionImplField.Instance);
    }

    protected override void CreateDomainMethods(EntryPointSelector.Model model, ClassDefinition classDefinition) {
        var invokeMethod = classDefinition.AddMethod("Invoke");
        invokeMethod.Modifiers = ComponentModifier.Public;
        invokeMethod.SetReturnType(TypeDefinition.Task(typeof(Stream)));

        var inputStream = invokeMethod.AddParameter(typeof(Stream), "inputStream");
        var lambdaContext = invokeMethod.AddParameter(KnownTypes.Lambda.ILambdaContext, "lambdaContext");

        var lambdaFunctionImplField = classDefinition.Fields.First(f => f.Name == "_lambdaFunctionImplService");

        IOutputComponent invokeStatement =
            lambdaFunctionImplField.Instance.Invoke("InvokeFunction", inputStream, lambdaContext);

        invokeMethod.Return(invokeStatement);
    }

    protected override ITypeDefinition LoggerHelper => KnownTypes.Lambda.LambdaLoggerHelper;

}