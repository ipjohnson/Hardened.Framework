using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Web.Lambda.SourceGenerator
{
    public static class ApplicationFileWriter
    {
        public static string WriteFile(ITypeDefinition applicationType)
        {
            var applicationFile = new CSharpFileDefinition(applicationType.Namespace);

            CreateApplicationClass(applicationFile, applicationType);

            var context = new OutputContext();

            applicationFile.WriteOutput(context);

            return context.Output();
        }

        private static void CreateApplicationClass(CSharpFileDefinition applicationFile, ITypeDefinition applicationType)
        {
            var appClass = applicationFile.AddClass(applicationType.Name);

            appClass.Modifiers |= ComponentModifier.Partial;

            appClass.AddBaseType(KnownTypes.Application.IApplicationRoot);

            var field = appClass.AddField(KnownTypes.Lambda.IApiGatewayEventProcessor, "_eventProcessor");

            field.Modifiers |= ComponentModifier.Readonly | ComponentModifier.Private;
            
            var provider = appClass.AddProperty(KnownTypes.DI.ServiceProvider, "Provider");

            provider.Set.Modifiers |= ComponentModifier.Private;

            CreateConstructors(appClass, applicationType, provider.Instance);

            CreateHandlerMethod(appClass, applicationType, provider.Instance, field.Instance);
        }

        private static void CreateHandlerMethod(ClassDefinition appClass, ITypeDefinition applicationType,
            InstanceDefinition providerInstance, InstanceDefinition eventProcessor)
        {
            var handler = appClass.AddMethod("FunctionHandlerAsync");

            handler.SetReturnType(new GenericTypeDefinition(typeof(Task<>),
                new[] { KnownTypes.Lambda.APIGatewayHttpApiV2ProxyResponse }));

            handler.AddAttribute(
                KnownTypes.Lambda.LambdaSerializer, "typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer)");

            var request = handler.AddParameter(KnownTypes.Lambda.APIGatewayHttpApiV2ProxyRequest, "request");
            var context = handler.AddParameter(KnownTypes.Lambda.ILambdaContext, "context");

            handler.Return(eventProcessor.Invoke("Process", request, context));
        }

        private static void CreateConstructors(ClassDefinition appClass, ITypeDefinition applicationType,
            InstanceDefinition providerInstanceDefinition)
        {
            appClass.AddConstructor(This(Null()));

            var constructor = appClass.AddConstructor();

            var overrides = 
                constructor.AddParameter(TypeDefinition.Action(KnownTypes.DI.IServiceCollection).MakeNullable(), "overrideDependencies");

            constructor.Assign(Invoke("CreateServiceProvider", overrides)).To(providerInstanceDefinition);
            
            constructor.AddIndentedStatement(
                Invoke(KnownTypes.Application.ApplicationLogic, "StartWithWait", providerInstanceDefinition,
                15));

            var handler = 
                constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                new[] { KnownTypes.Web.IWebExecutionHandlerService })).ToVar("handler");
            
            var middleware = 
                constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                    new[] { KnownTypes.Requests.IMiddlewareService })).ToVar("middleware");

            constructor.AddIndentedStatement(middleware.Invoke("Use", "_ => handler"));

            constructor.Assign(providerInstanceDefinition.InvokeGeneric("GetRequiredService",
                    new[] { KnownTypes.Lambda.IApiGatewayEventProcessor })).To("_eventProcessor");
        }
    }
}
