using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared;

public static class KnownTypes {
    public static class Namespace {
        public static class Amazon {
            public const string LambdaCore = "Amazon.Lambda.Core";

            public const string LambdaAPIGatewayEvents = "Amazon.Lambda.APIGatewayEvents";

            public const string LambdaSerializationSystemTextJson =
                "Amazon.Lambda.Serialization.SystemTextJson";
        }

        public static class Microsoft {
            public static class Extensions {
                public const string Logging = "Microsoft.Extensions.Logging";

                public const string DependencyInjection = "Microsoft.Extensions.DependencyInjection";
            }
        }

        public static class Hardened {
            public static class Amz {
                public static class Lambda {
                    public const string LambdaRuntime = "Hardened.Amz.Function.Lambda.Runtime";

                    public const string LambdaRuntimeDI = "Hardened.Amz.Function.Lambda.Runtime.DependencyInjection";

                    public const string LambdaRuntimeImpl = "Hardened.Amz.Function.Lambda.Runtime.Impl";
                }

                public static class Web {
                    public const string LambdaRuntimeImpl = "Hardened.Amz.Web.Lambda.Runtime.Impl";

                    public const string LambdaRuntimeDependencyInjection =
                        "Hardened.Amz.Web.Lambda.Runtime.DependencyInjection";
                }
            }

            public static class Commands {
                public const string NS = "Hardened.Commands";
                public const string Impl = "Hardened.Commands.Impl";
                public const string Attributes = "Hardened.Commands.Attributes";
            }

            public static class Web {
                public const string Runtime = "Hardened.Web.Runtime";

                public const string RuntimeDependencyInjection = "Hardened.Web.Runtime.DependencyInjection";

                public const string RuntimeHandlers = "Hardened.Web.Runtime.Handlers";

                public const string Routing = "Hardened.Web.Runtime.Routing";
            }

            public static class Requests {
                public static class Abstract {
                    public const string Attributes = "Hardened.Requests.Abstract.Attributes";

                    public const string Execution = "Hardened.Requests.Abstract.Execution";

                    public const string Middleware = "Hardened.Requests.Abstract.Middleware";

                    public const string Serializer = "Hardened.Requests.Abstract.Serializer";

                    public const string PathTokens = "Hardened.Requests.Abstract.PathTokens";
                }

                public static class Runtime {
                    public const string DependencyInjection = "Hardened.Requests.Runtime.DependencyInjection";

                    public const string Errors = "Hardened.Requests.Runtime.Errors";

                    public const string Execution = "Hardened.Requests.Runtime.Execution";

                    public const string Serializer = "Hardened.Requests.Runtime.Serializer";

                    public const string PathTokens = "Hardened.Requests.Runtime.PathTokens";
                }
            }

            public static class Shared {
                public static class Runtime {
                    public const string Application = "Hardened.Shared.Runtime.Application";

                    public const string Attributes = "Hardened.Shared.Runtime.Attributes";

                    public const string Configuration = "Hardened.Shared.Runtime.Configuration";

                    public const string DependencyInjection = "Hardened.Shared.Runtime.DependencyInjection";

                    public const string Logging = "Hardened.Shared.Runtime.Logging";
                }
            }

            public static class Templates {
                public const string Abstract = "Hardened.Templates.Abstract";

                public static class Runtime {
                    public const string DependencyInjection = "Hardened.Templates.Runtime.DependencyInjection";

                    public const string Impl = "Hardened.Templates.Runtime.Impl";

                    public const string Value = "Hardened.Templates.Runtime";
                }
            }
        }
    }

    public static class Configuration {
        public static readonly ITypeDefinition ConfigurationModelAttribute =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.Attributes, "ConfigurationModelAttribute");

        public static readonly ITypeDefinition IConfigurationManager =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Configuration,
                "IConfigurationManager");

        public static readonly ITypeDefinition IConfigurationPackage =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Configuration,
                "IConfigurationPackage");

        public static readonly ITypeDefinition IConfigurationValueProvider =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Configuration,
                "IConfigurationValueProvider");

        public static readonly ITypeDefinition IConfigurationValueAmender =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Configuration,
                "IConfigurationValueAmender");

        public static readonly ITypeDefinition IAppConfig =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Configuration,
                "IAppConfig");

        public static readonly ITypeDefinition AppConfig =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.Configuration, "AppConfig");
    }

    public static class DI {
        public static readonly ITypeDefinition IServiceCollection =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition,
                Namespace.Microsoft.Extensions.DependencyInjection, "IServiceCollection");

        public static readonly ITypeDefinition ServiceCollection =
            TypeDefinition.Get(Namespace.Microsoft.Extensions.DependencyInjection, "ServiceCollection");

        public static readonly ITypeDefinition IServiceProvider =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, "System", "IServiceProvider");

        public static readonly ITypeDefinition ServiceProvider =
            TypeDefinition.Get("System", "ServiceProvider");

        public static readonly ITypeDefinition ExposeAttribute =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.Attributes, "ExposeAttribute");

        public static readonly ITypeDefinition DependencyRegistry =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.DependencyInjection, "DependencyRegistry");

        public static class Registry {
            public static readonly ITypeDefinition LambdaWebDI =
                TypeDefinition.Get(Namespace.Hardened.Amz.Web.LambdaRuntimeDependencyInjection, "LambdaWebDI");

            public static readonly ITypeDefinition StandardDependencies =
                TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.DependencyInjection, "StandardDependencies");

            public static readonly ITypeDefinition RequestRuntimeDI =
                TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.DependencyInjection, "RequestRuntimeDI");

            public static readonly ITypeDefinition TemplateDI =
                TypeDefinition.Get(Namespace.Hardened.Templates.Runtime.DependencyInjection, "TemplateDI");

            public static readonly ITypeDefinition WebRuntimeDI =
                TypeDefinition.Get(Namespace.Hardened.Web.RuntimeDependencyInjection, "WebRuntimeDI");

            public static readonly ITypeDefinition LambdaFunctionRuntimeDI =
                TypeDefinition.Get(Namespace.Hardened.Amz.Lambda.LambdaRuntimeDI, "LambdaFunctionDI");
        }
    }

    public static class Application {
        public static readonly ITypeDefinition IApplicationRoot =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Application,
                "IApplicationRoot");

        public static readonly ITypeDefinition IStartupService =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Application,
                "IStartupService");

        public static readonly ITypeDefinition ApplicationLogic =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.Application, "ApplicationLogic");

        public static readonly ITypeDefinition IHardenedEnvironment =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Application,
                "IHardenedEnvironment");

        public static readonly ITypeDefinition EnvironmentImpl =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.Application, "EnvironmentImpl");

        public static readonly ITypeDefinition IApplicationModule =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Shared.Runtime.Application,
                "IApplicationModule");
    }

    public static class Requests {
        public static readonly ITypeDefinition HardenedFunctionAttribute =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Attributes,
                "HardenedFunctionAttribute");

        public static readonly ITypeDefinition IExecutionContext =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionContext");

        public static readonly ITypeDefinition IExecutionRequest =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionRequest");

        public static readonly ITypeDefinition IExecutionResponse =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionResponse");

        public static ITypeDefinition DefaultOutputFunc =
            TypeDefinition.Get(Namespace.Hardened.Requests.Abstract.Execution, "DefaultOutputFunc");

        public static readonly ITypeDefinition IExecutionFilter =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionFilter");

        public static readonly ITypeDefinition ExecutionHelper =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.Execution, "ExecutionHelper");

        public static readonly ITypeDefinition RawOutputHelper =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.Serializer, "RawOutputHelper");

        public static ITypeDefinition BaseExecutionHandler =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.Execution, "BaseExecutionHandler");

        public static readonly ITypeDefinition ExecutionRequestHandlerInfo =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.Execution, "ExecutionRequestHandlerInfo");

        public static readonly ITypeDefinition ExecutionRequestParameter =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.Execution, "ExecutionRequestParameter");

        public static readonly ITypeDefinition IExecutionRequestHandler =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionRequestHandler");

        public static readonly ITypeDefinition IExecutionRequestHandlerInfo =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionRequestHandlerInfo");

        public static readonly ITypeDefinition IMiddlewareService =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Middleware,
                "IMiddlewareService");

        public static readonly ITypeDefinition FilterFunc =
            new GenericTypeDefinition(typeof(Func<,>), new[] { IExecutionContext, IExecutionFilter }, false);

        public static readonly ITypeDefinition FilterFuncArray =
            new GenericTypeDefinition(typeof(Func<,>), new[] { IExecutionContext, IExecutionFilter }, true);

        public static readonly ITypeDefinition IExecutionChain =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionChain");

        public static readonly ITypeDefinition ControllerErrorHelper =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.Errors, "ControllerErrorHelper");

        public static readonly ITypeDefinition IExecutionRequestParameters =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionRequestParameters");

        public static readonly ITypeDefinition IContextSerializationService =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Serializer,
                "IContextSerializationService");

        public static readonly ITypeDefinition IExecutionRequestParameter =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Requests.Abstract.Execution,
                "IExecutionRequestParameter");

        public static readonly ITypeDefinition IReadOnlyListExecutionRequestParameter =
            new GenericTypeDefinition(typeof(IReadOnlyList<>), new[] { IExecutionRequestParameter });

        public static readonly ITypeDefinition PathTokenCollection =
            TypeDefinition.Get(Namespace.Hardened.Requests.Runtime.PathTokens, "PathTokenCollection");

        public static readonly ITypeDefinition PathToken =
            TypeDefinition.Get(Namespace.Hardened.Requests.Abstract.PathTokens, "PathToken");
    }

    public static class Logging {
        public static ITypeDefinition ILoggerT(ITypeDefinition typeDefinition) {
            return new GenericTypeDefinition(TypeDefinitionEnum.InterfaceDefinition,
                Namespace.Microsoft.Extensions.Logging, "ILogger", new[] { typeDefinition });
        }

        public static ITypeDefinition ILogger =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Microsoft.Extensions.Logging,
                "ILogger");

        public static readonly ITypeDefinition ILoggerFactory =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Microsoft.Extensions.Logging,
                "ILoggerFactory");

        public static ITypeDefinition ILoggingBuilder =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Microsoft.Extensions.Logging,
                "ILoggingBuilder");

        public static ITypeDefinition HardenedLoggingBuilder =
            TypeDefinition.Get(Namespace.Hardened.Shared.Runtime.Logging, "HardenedLoggingBuilder");
    }

    public static class Templates {
        public static ITypeDefinition ITemplateExecutionService { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateExecutionService");

        public static ITypeDefinition ITemplateOutputWriter { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateOutputWriter");

        public static ITypeDefinition ITemplateExecutionHandler { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateExecutionHandler");

        public static ITypeDefinition ITemplateExecutionContext { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateExecutionContext");

        public static ITypeDefinition IInternalTemplateServices { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "IInternalTemplateServices");

        public static ITypeDefinition ITemplateExecutionHandlerProvider { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateExecutionHandlerProvider");

        public static ITypeDefinition IStringEscapeService { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "IStringEscapeService");

        public static ITypeDefinition DefaultOutputFuncHelper { get; } =
            TypeDefinition.Get(Namespace.Hardened.Templates.Runtime.Impl, "DefaultOutputFuncHelper");

        public static ITypeDefinition ITemplateHelperProvider { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateHelperProvider");

        public static ITypeDefinition TemplateExecutionContext { get; } =
            TypeDefinition.Get(Namespace.Hardened.Templates.Runtime.Impl, "TemplateExecutionContext");

        public static ITypeDefinition TemplateHelperFactory { get; } =
            TypeDefinition.Get(Namespace.Hardened.Templates.Abstract, "TemplateHelperFactory");


        public static ITypeDefinition TemplateExecutionService { get; } =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Templates.Abstract,
                "ITemplateExecutionService");

        public static ITypeDefinition TemplateExecutionFunction { get; } =
            TypeDefinition.Get(Namespace.Hardened.Templates.Abstract, "TemplateExecutionFunction");

        public static ITypeDefinition TemplateHelperAttribute { get; } =
            TypeDefinition.Get(Namespace.Hardened.Templates.Abstract, "TemplateHelperAttribute");

        public static ITypeDefinition DefaultHelpers { get; } =
            TypeDefinition.Get("Hardened.Templates.Runtime.Helpers", "DefaultHelpers");
    }

    public class Web {
        public static readonly ITypeDefinition IWebExecutionRequestHandlerProvider =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Web.RuntimeHandlers,
                "IWebExecutionRequestHandlerProvider");

        public static readonly ITypeDefinition IWebExecutionHandlerService =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Web.RuntimeHandlers,
                "IWebExecutionHandlerService");

        public static readonly ITypeDefinition FilterRegistryStartupService =
            TypeDefinition.Get(Namespace.Hardened.Web.RuntimeDependencyInjection, "FilterRegistryStartupService");

        public static readonly ITypeDefinition RequestHandlerInfo =
            TypeDefinition.Get(Namespace.Hardened.Web.RuntimeHandlers, "RequestHandlerInfo");
    }

    public static class Lambda {
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
            TypeDefinition.Get(Namespace.Hardened.Amz.Web.LambdaRuntimeImpl, "IApiGatewayEventProcessor");

        public static readonly ITypeDefinition ILambdaFunctionImplService =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Amz.Lambda.LambdaRuntimeImpl,
                "ILambdaFunctionImplService");

        public static readonly ITypeDefinition LambdaInvokeFilter =
            TypeDefinition.Get(Namespace.Hardened.Amz.Lambda.LambdaRuntimeImpl, "LambdaInvokeFilter");

        public static readonly ITypeDefinition ILambdaHandler =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Amz.Lambda.LambdaRuntimeImpl,
                "ILambdaHandler");

        public static readonly ITypeDefinition IApiGatewayV2Handler =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Amazon.LambdaAPIGatewayEvents,
                "IApiGatewayV2Handler");

        public static readonly ITypeDefinition ILambdaHandlerPackage =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Amz.Lambda.LambdaRuntimeImpl,
                "ILambdaHandlerPackage");


        public static readonly ITypeDefinition ILambdaInvokeFilterProvider =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, Namespace.Hardened.Amz.Lambda.LambdaRuntimeImpl,
                "ILambdaInvokeFilterProvider");

        public static readonly ITypeDefinition LambdaLoggerHelper =
            TypeDefinition.Get("Hardened.Shared.Lambda.Runtime.Logging", "LambdaLoggerHelper");

        public static readonly ITypeDefinition ILambdaContextAccessor =
            TypeDefinition.Get("Hardened.Shared.Lambda.Runtime.Execution", "ILambdaContextAccessor");
    }

    public class System {
        public static ITypeDefinition CharSpan =
            new GenericTypeDefinition(typeof(Span<>), new[] { TypeDefinition.Get(typeof(string)) });

        public static ITypeDefinition IAsyncDisposable =
            TypeDefinition.Get(TypeDefinitionEnum.InterfaceDefinition, "System", "IAsyncDisposable");
    }
    
    public static class Commands {
        
        public static readonly ITypeDefinition CommandAttribute =
            TypeDefinition.Get(Namespace.Hardened.Commands.Attributes, "CommandAttribute");
    }
}