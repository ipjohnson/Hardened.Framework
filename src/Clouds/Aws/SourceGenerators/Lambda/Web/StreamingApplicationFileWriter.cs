using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

public class StreamingLambdaWebApplicationFileWriter : ApplicationEntryPointFileWriter {

    private static class StreamingKnownTypes {
        public static readonly ITypeDefinition ILambdaInvokeEngine =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition,
                "Hardened.Amz.Web.Lambda.Streaming.Impl", "ILambdaInvokeEngine");
    }

    protected override void ImplementApplicationRoot(EntryPointSelector.Model model, ClassDefinition classDefinition) {
        base.ImplementApplicationRoot(model, classDefinition);
    }

    protected override void CustomConstructorLogic(EntryPointSelector.Model entryPoint, ClassDefinition appClass,
        ConstructorDefinition constructor,
        ParameterDefinition environment) {
        var providerInstanceDefinition =
            appClass.Fields.First(f => f.Name == RootServiceProvider).Instance;

        var handler =
            constructor.Assign(RequiredModuleService.Resolve(
                KnownTypes.Web.IWebExecutionHandlerService,
                RootServiceProvider,
                entryPoint.EntryPointType.Name,
                "StreamingLambdaWebModule")).ToVar("handler");

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
                new[] { StreamingKnownTypes.ILambdaInvokeEngine })
        ).ToVar("engine");

        mainMethod.AddIndentedStatement(
            Await(engine.Invoke("InvokeAsync",
                CodeOutputComponent.Get("System.Threading.CancellationToken.None"))));
    }

}

public static class StreamingApplicationFileWriter {
    public static string WriteFile(EntryPointSelector.Model entryPoint) {
        var applicationFile = new CSharpFileDefinition(entryPoint.EntryPointType.Namespace);
        var lambdaFileWriter = new StreamingLambdaWebApplicationFileWriter();

        lambdaFileWriter.CreateApplicationClass(entryPoint, applicationFile);

        var context = new OutputContext(
            new OutputContextOptions {
                TypeOutputMode = TypeOutputMode.Global
            });

        applicationFile.WriteOutput(context);

        return context.Output();
    }
}
