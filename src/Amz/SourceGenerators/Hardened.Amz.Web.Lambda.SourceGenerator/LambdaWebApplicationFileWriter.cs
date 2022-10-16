using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

public class LambdaWebApplicationFileWriter : ApplicationEntryPointFileWriter
{
    protected override void CustomConstructorLogic(EntryPointSelector.Model entryPoint, ClassDefinition appClass, ConstructorDefinition constructor,
        ParameterDefinition environment)
    {
        var providerInstanceDefinition = 
            appClass.Fields.First(f => f.Name == RootServiceProvider).Instance;

        var handler =
            constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Web.IWebExecutionHandlerService })).ToVar("handler");

        var middleware =
            constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Requests.IMiddlewareService })).ToVar("middleware");

        constructor.AddIndentedStatement(middleware.Invoke("Use", "_ => handler"));

        var eventProcessor = 
            appClass.AddField(KnownTypes.Lambda.IApiGatewayEventProcessor, "_eventProcessor");

        constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
            new[] { KnownTypes.Lambda.IApiGatewayEventProcessor })).To(eventProcessor.Instance);
    }

    protected override ITypeDefinition LoggerHelper => KnownTypes.Lambda.LambdaLoggerHelper;

    protected override void CreateDomainMethods(EntryPointSelector.Model model, ClassDefinition classDefinition)
    {
        var eventProcessor = classDefinition.Fields.First(f => f.Name == "_eventProcessor");

        classDefinition.AddBaseType(KnownTypes.Lambda.IApiGatewayV2Handler);

        var handler = classDefinition.AddMethod("Invoke");

        handler.SetReturnType(new GenericTypeDefinition(typeof(Task<>),
            new[] { KnownTypes.Lambda.APIGatewayHttpApiV2ProxyResponse }));

        handler.AddAttribute(
            KnownTypes.Lambda.LambdaSerializer, "typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer)");

        var request = handler.AddParameter(KnownTypes.Lambda.APIGatewayHttpApiV2ProxyRequest, "request");
        var context = handler.AddParameter(KnownTypes.Lambda.ILambdaContext, "context");

        handler.Return(eventProcessor.Instance.Invoke("Process", request, context));
    }

    protected override IEnumerable<ITypeDefinition> RegisterDiTypes()
    {
        yield return KnownTypes.DI.Registry.LambdaWebDI;
    }
}