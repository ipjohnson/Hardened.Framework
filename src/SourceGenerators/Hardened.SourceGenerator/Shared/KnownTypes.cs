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

            public static class Microsoft
            {
                public static class Extensions
                {
                    public const string Logging = "Microsoft.Extensions.Logging";

                    public const string DependencyInjection = "Microsoft.Extensions.DependencyInjection";
                }
            }

            public static class Hardened
            {
                public static class Lambda
                {
                    public const string LambdaRuntime = "Hardened.Function.Lambda.Runtime";

                    public const string LambdaRuntimeDI = "Hardened.Function.Lambda.Runtime.DependencyInjection";

                    public const string LambdaRuntimeImpl = "Hardened.Function.Lambda.Runtime.Impl";
                }

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

            public const string HardenedSharedRuntimeConfiguration = "Hardened.Shared.Runtime.Configuration";

            public const string HardenedSharedRuntimeDependencyInjection = "Hardened.Shared.Runtime.DependencyInjection";

            public const string HardenedSharedRuntimeLogging = "Hardened.Shared.Runtime.Logging";

            public const string HardenedRequestsRuntimeErrors = "Hardened.Requests.Runtime.Errors";

            public const string HardenedRequestsRuntimeExecution = "Hardened.Requests.Runtime.Execution";

            public const string HardenedRequestsRuntimeDependencyInjection = "Hardened.Requests.Runtime.DependencyInjection";

            public const string HardenedRequestsAbstractExecution = "Hardened.Requests.Abstract.Execution";

            public const string HardenedRequestsAbstractMiddleware = "Hardened.Requests.Abstract.Middleware";

            public const string HardenedRequestsAbstractSerializer = "Hardened.Requests.Abstract.Serializer";

            public static readonly string HardenedTemplateAbstractNamespace = "Hardened.Templates.Abstract";

            public static readonly string HardenedTemplateRuntimeImplNamespace = "Hardened.Templates.Runtime.Impl";

            public static readonly string HardenedTemplateRuntimeDependencyInjection = "Hardened.Templates.Runtime.DependencyInjection";

            public static readonly string HardenedTemplateRuntimeNamespace = "Hardened.Templates.Runtime";

        }
        
        public static class Configuration
        {
            public static readonly ITypeDefinition ConfigurationModelAttribute =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeAttributes, "ConfigurationModelAttribute");

            public static readonly ITypeDefinition IConfigurationManager =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeConfiguration, "IConfigurationManager");

            public static readonly ITypeDefinition IConfigurationPackage = 
                    TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeConfiguration, "IConfigurationPackage");

            public static readonly ITypeDefinition IConfigurationValueProvider =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeConfiguration, "IConfigurationValueProvider");

            public static readonly ITypeDefinition IConfigurationValueAmender =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeConfiguration, "IConfigurationValueAmender");

            public static readonly ITypeDefinition IAppConfig =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeConfiguration, "IAppConfig");

            public static readonly ITypeDefinition AppConfig =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeConfiguration, "AppConfig");
        }

        public static class DI
        {
            public static readonly ITypeDefinition IServiceCollection =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Microsoft.Extensions.DependencyInjection, "IServiceCollection");

            public static readonly ITypeDefinition ServiceCollection =
                TypeDefinition.Get(Namespace.Microsoft.Extensions.DependencyInjection, "ServiceCollection");

            public static readonly ITypeDefinition IServiceProvider =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, "System", "IServiceProvider");

            public static readonly ITypeDefinition ServiceProvider =
                TypeDefinition.Get("System", "ServiceProvider");

            public static readonly ITypeDefinition ExposeAttribute =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeAttributes, "ExposeAttribute");

            public static readonly ITypeDefinition DependencyRegistry =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeDependencyInjection, "DependencyRegistry");

            public static class Registry
            {
                public static readonly ITypeDefinition LambdaWebDI =
                    TypeDefinition.Get(Namespace.Hardened.Web.LambdaRuntimeDependencyInjection, "LambdaWebDI");

                public static readonly ITypeDefinition StandardDependencies =
                    TypeDefinition.Get(Namespace.HardenedSharedRuntimeDependencyInjection, "StandardDependencies");

                public static readonly ITypeDefinition RequestRuntimeDI =
                    TypeDefinition.Get(Namespace.HardenedRequestsRuntimeDependencyInjection, "RequestRuntimeDI");

                public static readonly ITypeDefinition TemplateDI =
                    TypeDefinition.Get(Namespace.HardenedTemplateRuntimeDependencyInjection, "TemplateDI");

                public static readonly ITypeDefinition WebRuntimeDI =
                    TypeDefinition.Get(Namespace.Hardened.Web.RuntimeDependencyInjection, "WebRuntimeDI");

                public static readonly ITypeDefinition LambdaFunctionRuntimeDI =
                    TypeDefinition.Get(Namespace.Hardened.Lambda.LambdaRuntimeDI, "LambdaFunctionDI");
            }
        }

        public static class Application
        {
            public static readonly ITypeDefinition IApplicationRoot =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeApplication, "IApplicationRoot");

            public static readonly ITypeDefinition IStartupService =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeApplication, "IStartupService");

            public static readonly ITypeDefinition ApplicationLogic =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeApplication, "ApplicationLogic");

            public static readonly ITypeDefinition IEnvironment =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeApplication, "IEnvironment");

            public static readonly ITypeDefinition EnvironmentImpl =
                TypeDefinition.Get(Namespace.HardenedSharedRuntimeApplication, "EnvironmentImpl");

            public static readonly ITypeDefinition IApplicationModule =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedSharedRuntimeApplication, "IApplicationModule");

        }

        public static class Requests
        {
            public static readonly ITypeDefinition IExecutionContext =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionContext");
            
            public static readonly ITypeDefinition IExecutionRequest =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionRequest");

            public static readonly ITypeDefinition IExecutionResponse =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionResponse");
            
            public static ITypeDefinition DefaultOutputFunc =
                TypeDefinition.Get(Namespace.HardenedRequestsAbstractExecution, "DefaultOutputFunc");

            public static readonly ITypeDefinition IExecutionFilter =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionFilter");

            public static readonly ITypeDefinition ExecutionHelper =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "ExecutionHelper");

            public static ITypeDefinition BaseExecutionHandler =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "BaseExecutionHandler");

            public static readonly ITypeDefinition ExecutionRequestHandlerInfo =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "ExecutionRequestHandlerInfo");

            public static readonly ITypeDefinition ExecutionRequestParameter =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeExecution, "ExecutionRequestParameter");

            public static readonly ITypeDefinition IExecutionRequestHandler =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionRequestHandler");

            public static readonly ITypeDefinition IExecutionRequestHandlerInfo =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionRequestHandlerInfo");

            public static readonly ITypeDefinition IMiddlewareService =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractMiddleware, "IMiddlewareService");

            public static readonly ITypeDefinition FilterFunc =
                new GenericTypeDefinition(typeof(Func<,>), new[] { IExecutionContext, IExecutionFilter }, false);

            public static readonly ITypeDefinition FilterFuncArray =
                new GenericTypeDefinition(typeof(Func<,>), new[] { IExecutionContext, IExecutionFilter }, true);

            public static readonly ITypeDefinition IExecutionChain = 
                    TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionChain");

            public static readonly ITypeDefinition ControllerErrorHelper =
                TypeDefinition.Get(Namespace.HardenedRequestsRuntimeErrors, "ControllerErrorHelper");

            public static readonly ITypeDefinition IExecutionRequestParameters =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionRequestParameters");

            public static readonly ITypeDefinition IContextSerializationService =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractSerializer, "IContextSerializationService");

            public static readonly ITypeDefinition IExecutionRequestParameter =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedRequestsAbstractExecution, "IExecutionRequestParameter");

            public static readonly ITypeDefinition IReadOnlyListExecutionRequestParameter =
                new GenericTypeDefinition(typeof(IReadOnlyList<>), new []{IExecutionRequestParameter});
        }

        public static class Logging
        {
            public static ITypeDefinition ILoggerT(ITypeDefinition typeDefinition)
            {
                return new GenericTypeDefinition(TypeDefinitionEnum.InterfaceDefinition, 
                    Namespace.Microsoft.Extensions.Logging, "ILogger", new[] { typeDefinition });
            }

            public static ITypeDefinition ILogger =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Microsoft.Extensions.Logging, "ILogger");

            public static readonly ITypeDefinition ILoggerFactory =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Microsoft.Extensions.Logging, "ILoggerFactory");
            
        }

        public static class Templates
        {
            public static ITypeDefinition ITemplateExecutionService { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionService");

            public static ITypeDefinition ITemplateOutputWriter { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateOutputWriter");

            public static ITypeDefinition ITemplateExecutionHandler { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionHandler");

            public static ITypeDefinition ITemplateExecutionContext { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionContext");

            public static ITypeDefinition IInternalTemplateServices { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "IInternalTemplateServices");

            public static ITypeDefinition ITemplateExecutionHandlerProvider { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionHandlerProvider");

            public static ITypeDefinition IStringEscapeService { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "IStringEscapeService");

            public static ITypeDefinition DependencyInjectionRegistration { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateRuntimeImplNamespace, "DependencyInjectionRegistration");

            public static ITypeDefinition DefaultOutputFuncHelper { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateRuntimeImplNamespace, "DefaultOutputFuncHelper");

                public static ITypeDefinition ITemplateHelperProvider { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateHelperProvider");

            public static ITypeDefinition TemplateExecutionContext { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateRuntimeImplNamespace, "TemplateExecutionContext");

            public static ITypeDefinition TemplateHelperFactory { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "TemplateHelperFactory");


            public static ITypeDefinition TemplateExecutionService { get; } =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.HardenedTemplateAbstractNamespace, "ITemplateExecutionService");

            public static ITypeDefinition TemplateExecutionFunction { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "TemplateExecutionFunction");

            public static ITypeDefinition TemplateHelperAttribute { get; } =
                TypeDefinition.Get(Namespace.HardenedTemplateAbstractNamespace, "TemplateHelperAttribute");

            public static ITypeDefinition DefaultHelpers { get; } =
                TypeDefinition.Get("Hardened.Templates.Runtime.Helpers", "DefaultHelpers");


        }

        public class Web
        {
            public static readonly ITypeDefinition IWebExecutionRequestHandlerProvider = 
                    TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Web.RuntimeHandlers, "IWebExecutionRequestHandlerProvider");

            public static readonly ITypeDefinition IWebExecutionHandlerService =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Web.RuntimeHandlers, "IWebExecutionHandlerService");

            public static readonly ITypeDefinition FilterRegistryStartupService =
                TypeDefinition.Get(Namespace.Hardened.Web.RuntimeDependencyInjection, "FilterRegistryStartupService");

        }

        public static class Lambda
        {
            public static readonly ITypeDefinition LambdaSerializer =
                TypeDefinition.Get(Namespace.Amazon.LambdaCore, "LambdaSerializerAttribute");

            public static readonly ITypeDefinition ILambdaContext =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Amazon.LambdaCore, "ILambdaContext");

            public static ITypeDefinition DefaultLambdaJsonSerializer =
                TypeDefinition.Get(Namespace.Amazon.LambdaSerializationSystemTextJson, "DefaultLambdaJsonSerializer");

            public static readonly ITypeDefinition APIGatewayHttpApiV2ProxyRequest =
                TypeDefinition.Get(Namespace.Amazon.LambdaAPIGatewayEvents, "APIGatewayHttpApiV2ProxyRequest");

            public static readonly ITypeDefinition APIGatewayHttpApiV2ProxyResponse =
                TypeDefinition.Get(Namespace.Amazon.LambdaAPIGatewayEvents, "APIGatewayHttpApiV2ProxyResponse");

            public static readonly ITypeDefinition IApiGatewayEventProcessor =
                TypeDefinition.Get(Namespace.Hardened.Web.LambdaRuntimeImpl, "IApiGatewayEventProcessor");

            public static readonly ITypeDefinition ILambdaFunctionImplService =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Lambda.LambdaRuntimeImpl, "ILambdaFunctionImplService");

            public static readonly ITypeDefinition LambdaFunctionAttribute =
                TypeDefinition.Get(Namespace.Hardened.Lambda.LambdaRuntime, "LambdaFunctionAttribute");

            public static readonly ITypeDefinition LambdaInvokeFilter =
                TypeDefinition.Get(Namespace.Hardened.Lambda.LambdaRuntimeImpl, "LambdaInvokeFilter");

            public static readonly ITypeDefinition ILambdaHandler =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Lambda.LambdaRuntimeImpl, "ILambdaHandler");

            public static readonly ITypeDefinition IApiGatewayV2Handler =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Amazon.LambdaAPIGatewayEvents, "IApiGatewayV2Handler");

            public static readonly ITypeDefinition ILambdaHandlerPackage =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Lambda.LambdaRuntimeImpl,
                    "ILambdaHandlerPackage");


            public static readonly ITypeDefinition ILambdaInvokeFilterProvider =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Lambda.LambdaRuntimeImpl,
                    "ILambdaInvokeFilterProvider");

            public static readonly ITypeDefinition LambdaLoggerHelper =
                TypeDefinition.Get("Hardened.Shared.Lambda.Runtime.Logging", "LambdaLoggerHelper");

            public static readonly ITypeDefinition ILambdaContextAccessor =
            TypeDefinition.Get("Hardened.Shared.Lambda.Runtime.Execution", "ILambdaContextAccessor");
        }

        public class System
        {
            public static ITypeDefinition CharSpan =
                new GenericTypeDefinition(typeof(Span<>), new[] { TypeDefinition.Get(typeof(string)) });

            public static ITypeDefinition IAsyncDisposable =
                TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, "System","IAsyncDisposable");
        }
    }
}
