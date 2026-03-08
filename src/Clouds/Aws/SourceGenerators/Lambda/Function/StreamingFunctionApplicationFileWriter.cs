using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

public class StreamingLambdaFunctionApplicationFileWriter : ApplicationEntryPointFileWriter {

    private static class StreamingKnownTypes {
        public static readonly ITypeDefinition IFunctionInvokeEngine =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition,
                "Hardened.Amz.Function.Lambda.Streaming.Impl", "IFunctionInvokeEngine");
    }

    protected override void ImplementApplicationRoot(EntryPointSelector.Model model, ClassDefinition classDefinition) {
        base.ImplementApplicationRoot(model, classDefinition);

        var iAppModuleProvider = TypeDefinition.Get(
            TypeDefinitionEnum.InterfaceDefinition,
            KnownTypes.Namespace.Hardened.Shared.Runtime.Application,
            "IApplicationModuleProvider");

        classDefinition.AddBaseType(iAppModuleProvider);

        var provideModules = classDefinition.AddMethod("ProvideModules");
        provideModules.SetReturnType(
            TypeDefinition.IEnumerable(KnownTypes.Application.IApplicationModule));
        provideModules.AddIndentedStatement("yield return this");
    }

    protected override void CustomConstructorLogic(EntryPointSelector.Model entryPoint, ClassDefinition appClass,
        ConstructorDefinition constructor,
        ParameterDefinition environment) {
        var providerInstanceDefinition =
            appClass.Fields.First(f => f.Name == RootServiceProvider).Instance;

        var filterProvider =
            constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Lambda.ILambdaInvokeFilterProvider })).ToVar("filterProvider");

        constructor.Assign(filterProvider.Invoke("ProvideFilter", RootServiceProvider)).ToVar("handler");

        var middleware =
            constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Requests.IMiddlewareService })).ToVar("middleware");

        constructor.AddIndentedStatement(middleware.Invoke("Use", "_ => handler"));
    }

    protected override ITypeDefinition LoggerHelper => KnownTypes.Lambda.LambdaLoggerHelper;

    protected override void CreateDomainMethods(EntryPointSelector.Model model, ClassDefinition classDefinition) {
        var mainMethod = classDefinition.AddMethod("Main");

        mainMethod.Modifiers = ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Async;
        mainMethod.SetReturnType(typeof(Task));

        var argsParam = mainMethod.AddParameter(TypeDefinition.Get(typeof(string[])), "args");

        var app = mainMethod.Assign(New(model.EntryPointType)).ToVar("app");

        var providerInstance = classDefinition.Fields.First(f => f.Name == RootServiceProvider).Instance;

        var engine = mainMethod.Assign(
            app.Property(RootServiceProvider).InvokeGeneric("GetRequiredService",
                new[] { StreamingKnownTypes.IFunctionInvokeEngine })
        ).ToVar("engine");

        mainMethod.AddIndentedStatement(
            Await(engine.Invoke("InvokeAsync",
                CodeOutputComponent.Get("System.Threading.CancellationToken.None"))));
    }

}

public static class StreamingFunctionApplicationFileWriter {
    public static string WriteFile(EntryPointSelector.Model entryPoint) {
        var applicationFile = new CSharpFileDefinition(entryPoint.EntryPointType.Namespace);
        var lambdaFileWriter = new StreamingLambdaFunctionApplicationFileWriter();

        lambdaFileWriter.CreateApplicationClass(entryPoint, applicationFile);

        var context = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        applicationFile.WriteOutput(context);

        return context.Output();
    }
}
