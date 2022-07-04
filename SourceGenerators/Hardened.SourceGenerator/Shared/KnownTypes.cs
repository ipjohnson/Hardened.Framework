using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared
{
    public static class KnownTypes
    {
        public static class Namespace
        {
            public static class Amazon
            {
                public const string LambdaCore = "Amazon.Lambda.Core";

                public const string LambdaAPIGatewayEvents = "Amazon.Lambda.APIGatewayEvents";

                public const string LambdaSerializationSystemTextJson =
                    "Amazon.Lambda.Serialization.SystemTextJson";
            }

            public static class Hardened
            {
                public static class Web
                {
                    public const string Runtime = "Hardened.Web.Runtime";

                    public const string RuntimeDependencyInjection = "Hardened.Web.Runtime.DependencyInjection";

                    public const string RuntimeHandlers = "Hardened.Web.Runtime.Handlers";

                    public const string LambdaRuntimeDependencyInjection = "Hardened.Web.Lambda.Runtime.DependencyInjection";

                    public const string LambdaRuntimeImpl = "Hardened.Web.Lambda.Runtime.Impl";
                }
            }

            public const string HardenedSharedRuntimeApplication = "Hardened.Shared.Runtime.Application";

            public const string HardenedSharedRuntimeAttributes = "Hardened.Shared.Runtime.Attributes";

            public const string HardenedSharedRuntimeDependencyInjection = "Hardened.Shared.Runtime.DependencyInjection";

            public const string HardenedRequestsRuntimeErrors = "Hardened.Requests.Runtime.Errors";

            public const string HardenedRequestsRuntimeExecution = "Hardened.Requests.Runtime.Execution";

            public const string HardenedRequestsRuntimeDependencyInjection = "Hardened.Requests.Runtime.DependencyInjection";

            public const string HardenedRequestsAbstractExecution = "Hardened.Requests.Abstract.Execution";

            public const string HardenedRequestsAbstractMiddleware = "Hardened.Requests.Abstract.Middleware";

            public static string HardenedTemplateAbstractNamespace = "Hardened.Templates.Abstract";

            public static string HardenedTemplateRuntimeImplNamespace = "Hardened.Templates.Runtime.Impl";

            public static string HardenedTemplateRuntimeDependencyInjection = "Hardened.Templates.Runtime.DependencyInjection";

            public static string HardenedTemplateRuntimeNamespace = "Hardened.Templates.Runtime";
        }
        
        public static class DI
        {
            public static ITypeDefinition IServiceCollection =
                TypeDefinition.Get("Microsoft.Extensions.DependencyInjection", "IServiceCollection");

            public static ITypeDefinition ServiceCollection =
                TypeDefinition.Get("Microsoft.Extensions.DependencyInjection", "ServiceCollection");

            public static ITypeDefinition IServiceProvider =
                TypeDefinition.Get("System", "IServiceProvider");

            public static ITypeDefinition ExposeAttribute =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeAttributes, "ExposeAttribute");

            public static ITypeDefinition DependencyRegistry =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeDependencyInjection, "DependencyRegistry");

            public static class Registry
            {
                public static ITypeDefinition LambdaWebDI =
                    TypeDefinition.Get(Namespace.Hardened.Web.LambdaRuntimeDependencyInjection, "LambdaWebDI");

                public static ITypeDefinition StandardDependencies =
                    TypeDefinition.Get(Namespace.HardenedSharedRuntimeDependencyInjection, "StandardDependencies");

                public static ITypeDefinition RequestRuntimeDI =
                    TypeDefinition.Get(Namespace.HardenedRequestsRuntimeDependencyInjection, "RequestRuntimeDI");

                public static ITypeDefinition TemplateDI =
                    TypeDefinition.Get(Namespace.HardenedTemplateRuntimeDependencyInjection, "TemplateDI");

                public static ITypeDefinition WebRuntimeDI =
                    TypeDefinition.Get(Namespace.Hardened.Web.RuntimeDependencyInjection, "WebRuntimeDI");
            }
        }

        public static class Application
        {
            public static ITypeDefinition IApplicationRoot =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeApplication, "IApplicationRoot");

            public static ITypeDefinition ApplicationLogic =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeApplication, "ApplicationLogic");
        }

        public static class Requests
        {
            public static ITypeDefinition IExecutionContext =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "IExecutionContext");

            public static ITypeDefinition DefaultOutputFunc =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "DefaultOutputFunc");

            public static ITypeDefinition IExecutionFilter =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "IExecutionFilter");

            public static ITypeDefinition ExecutionHelper =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "ExecutionHelper");

            public static ITypeDefinition BaseExecutionHandler =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "BaseExecutionHandler");

            public static ITypeDefinition ExecutionRequestHandlerInfo =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "ExecutionRequestHandlerInfo");

            public static ITypeDefinition IExecutionRequestHandler =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "IExecutionRequestHandler");

            public static ITypeDefinition IExecutionRequestHandlerInfo =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "IExecutionRequestHandlerInfo");

            public static ITypeDefinition IMiddlewareService =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractMiddleware, "IMiddlewareService");

            public static ITypeDefinition FilterFunc =
                new GenericTypeDefinition(typeof(Func<,>), new[] { IExecutionContext, IExecutionFilter }, false);

            public static ITypeDefinition FilterFuncArray =
                new GenericTypeDefinition(typeof(Func<,>), new[] { IExecutionContext, IExecutionFilter }, true);

            public static ITypeDefinition IExecutionChain = 
                    TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "IExecutionChain");

            public static ITypeDefinition ControllerErrorHelper =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeErrors, "ControllerErrorHelper");
        }

        public static class Templates
        {
            public static ITypeDefinition ITemplateExecutionService { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionService");

            public static ITypeDefinition ITemplateOutputWriter { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateOutputWriter");

            public static ITypeDefinition ITemplateExecutionHandler { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionHandler");

            public static ITypeDefinition ITemplateExecutionContext { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionContext");

            public static ITypeDefinition IInternalTemplateServices { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "IInternalTemplateServices");

            public static ITypeDefinition ITemplateExecutionHandlerProvider { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionHandlerProvider");

            public static ITypeDefinition IStringEscapeService { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "IStringEscapeService");

            public static ITypeDefinition DependencyInjectionRegistration { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateRuntimeImplNamespace, "DependencyInjectionRegistration");

            public static ITypeDefinition DefaultOutputFuncHelper { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateRuntimeImplNamespace, "DefaultOutputFuncHelper");

                public static ITypeDefinition ITemplateHelperProvider { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateHelperProvider");

            public static ITypeDefinition TemplateExecutionContext { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateRuntimeImplNamespace, "TemplateExecutionContext");

            public static ITypeDefinition TemplateHelperFactory { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "TemplateHelperFactory");

            public static ITypeDefinition TemplateExecutionService { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionService");

            public static ITypeDefinition TemplateExecutionFunction { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "TemplateExecutionFunction");

            public static ITypeDefinition DefaultHelpers { get; } =
                TypeDefinition.Get("Hardened.Templates.Runtime.Helpers", "DefaultHelpers");
        }

        public class Web
        {
            public static ITypeDefinition IWebExecutionRequestHandlerProvider = 
                    TypeDefinition.Get(Namespace.Hardened.Web.RuntimeHandlers, "IWebExecutionRequestHandlerProvider");

            public static ITypeDefinition IWebExecutionHandlerService =
                TypeDefinition.Get(Namespace.Hardened.Web.RuntimeHandlers, "IWebExecutionHandlerService");

        }

        public static class Lambda
        {
            public static ITypeDefinition LambdaSerializer =
                TypeDefinition.Get(Namespace.Amazon.LambdaCore, "LambdaSerializerAttribute");

            public static ITypeDefinition ILambdaContext =
                TypeDefinition.Get(Namespace.Amazon.LambdaCore, "ILambdaContext");

            public static ITypeDefinition DefaultLambdaJsonSerializer =
                TypeDefinition.Get(Namespace.Amazon.LambdaSerializationSystemTextJson, "DefaultLambdaJsonSerializer");

            public static ITypeDefinition APIGatewayHttpApiV2ProxyRequest =
                TypeDefinition.Get(Namespace.Amazon.LambdaAPIGatewayEvents, "APIGatewayHttpApiV2ProxyRequest");

            public static ITypeDefinition APIGatewayHttpApiV2ProxyResponse =
                TypeDefinition.Get(Namespace.Amazon.LambdaAPIGatewayEvents, "APIGatewayHttpApiV2ProxyResponse");

            public static ITypeDefinition IApiGatewayEventProcessor =
                TypeDefinition.Get(Namespace.Hardened.Web.LambdaRuntimeImpl, "IApiGatewayEventProcessor");

        }

        public class System
        {
            public static ITypeDefinition CharSpan =
                new GenericTypeDefinition(typeof(Span<>), new[] { TypeDefinition.Get(typeof(string)) });
        }
    }
}
