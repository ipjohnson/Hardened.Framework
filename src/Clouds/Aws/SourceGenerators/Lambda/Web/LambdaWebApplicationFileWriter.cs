using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

public class LambdaWebApplicationFileWriter : ApplicationEntryPointFileWriter {
    private static class HostKnownTypes {
        public static readonly ITypeDefinition ILambdaWebHost =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition,
                "Hardened.Amz.Web.Lambda.Runtime.Impl", "ILambdaWebHost");

        public static readonly ITypeDefinition StreamingHandlerCheck =
            TypeDefinition.Get("Hardened.Amz.Web.Lambda.Runtime.Impl", "StreamingHandlerCheck");
    }

    private readonly IReadOnlyList<string> _streamingHandlers;

    /// <param name="streamingHandlers">
    /// The handlers carrying <c>[ServerSentEvents]</c>, by type and method, so the application can
    /// warn at startup when it is deployed in a mode that cannot deliver them.
    /// </param>
    public LambdaWebApplicationFileWriter(IReadOnlyList<string> streamingHandlers) {
        _streamingHandlers = streamingHandlers;
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
                "LambdaWebModule")).ToVar("handler");

        // GetRequiredService is an extension method, and an extension method is reachable only
        // through a using of its namespace - global:: cannot name one. CSharpAuthor 2.0 derives
        // the using list from the types actually written, so nothing supplies this for us.
        var resolveMiddleware = providerInstanceDefinition.InvokeGeneric("GetRequiredService",
            new[] { KnownTypes.Requests.IMiddlewareService });

        resolveMiddleware.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        var middleware = constructor.Assign(resolveMiddleware).ToVar("middleware");

        constructor.AddIndentedStatement(middleware.Invoke("Use", "_ => handler"));

        var eventProcessor =
            appClass.AddField(KnownTypes.Lambda.IApiGatewayEventProcessor, "_eventProcessor");

        var resolveProcessor = providerInstanceDefinition.InvokeGeneric("GetRequiredService",
            new[] { KnownTypes.Lambda.IApiGatewayEventProcessor });

        resolveProcessor.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        constructor.Assign(resolveProcessor).To(eventProcessor.Instance);

        CreateStreamingHandlerCheck(appClass, constructor);
    }

    /// <summary>
    /// The handlers that stream, named where the build can see them, and checked against the
    /// response mode where only the running application can see it.
    /// </summary>
    private void CreateStreamingHandlerCheck(ClassDefinition appClass, ConstructorDefinition constructor) {
        var streamingHandlers = appClass.AddField(typeof(string[]), "_streamingHandlers");

        streamingHandlers.Modifiers =
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

        // Sorted so the emitted file is the same whatever order the compilation walked the
        // handlers in, which is what keeps the incremental generator's output cached.
        var names = _streamingHandlers
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => (object)CodeOutputComponent.Get(QuoteString(name)))
            .ToArray();

        streamingHandlers.InitializeValue = names.Length == 0
            ? CodeOutputComponent.Get("global::System.Array.Empty<string>()")
            : NewArray(typeof(string), names);

        constructor.AddIndentedStatement(
            Invoke(HostKnownTypes.StreamingHandlerCheck, "Warn", RootServiceProvider, "_streamingHandlers"));
    }

    protected override ITypeDefinition LoggerHelper => KnownTypes.Lambda.LambdaLoggerHelper;

    protected override void CreateDomainMethods(EntryPointSelector.Model model, ClassDefinition classDefinition) {
        CreateInvoke(classDefinition);
        CreateMain(model, classDefinition);
    }

    /// <summary>
    /// The buffered handler, for the managed runtime's class-library handler shape and for tests
    /// and the local harness that drive the application directly.
    /// </summary>
    private static void CreateInvoke(ClassDefinition classDefinition) {
        var eventProcessor = classDefinition.Fields.First(f => f.Name == "_eventProcessor");

        classDefinition.AddBaseType(KnownTypes.Lambda.IApiGatewayV2Handler);

        var handler = classDefinition.AddMethod("Invoke");

        handler.SetReturnType(new GenericTypeDefinition(typeof(Task<>),
            new[] { KnownTypes.Lambda.APIGatewayHttpApiV2ProxyResponse }));

        handler.AddAttribute(
            KnownTypes.Lambda.LambdaSerializer,
            "typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer)");

        var request = handler.AddParameter(KnownTypes.Lambda.APIGatewayHttpApiV2ProxyRequest, "request");
        var context = handler.AddParameter(KnownTypes.Lambda.ILambdaContext, "context");

        handler.Return(eventProcessor.Instance.Invoke("Process", request, context));
    }

    /// <summary>
    /// The executable entry point: the application built, its host handed to the AWS bootstrap on
    /// the raw stream overload, and the invoke loop run until the runtime ends the process. The
    /// bootstrap owns polling, error reporting, the invocation id, SnapStart and concurrency.
    /// </summary>
    private static void CreateMain(EntryPointSelector.Model model, ClassDefinition classDefinition) {
        var mainMethod = classDefinition.AddMethod("Main");

        mainMethod.Modifiers = ComponentModifier.Public | ComponentModifier.Static | ComponentModifier.Async;
        mainMethod.SetReturnType(typeof(Task));

        mainMethod.AddParameter(TypeDefinition.Get(typeof(string[])), "args");

        var app = mainMethod.Assign(New(model.EntryPointType)).ToVar("app");

        var resolveHost = app.Property(RootServiceProvider).InvokeGeneric("GetRequiredService",
            new[] { HostKnownTypes.ILambdaWebHost });

        resolveHost.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);

        mainMethod.Assign(resolveHost).ToVar("host");

        mainMethod.AddIndentedStatement(BootstrapEmitter.Build("host.Invoke"));
        mainMethod.AddIndentedStatement(BootstrapEmitter.Run());
    }
}
