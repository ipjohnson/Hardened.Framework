using DependencyModules.Runtime.Helpers;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using TestApp;
using TestApp.Generated;

namespace TestApp
{
    public partial class Application
    {
        [DynamicDependency(nameof(RoutingTableDI))]
        private static int _routingTableDependencies =         DependencyRegistry<Application>.Add(RoutingTableDI)
;

        private static void RoutingTableDI(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<
                IWebExecutionRequestHandlerProvider,
                Application.RoutingTable
            >();
            serviceCollection.AddTransient<ContextController>();
            serviceCollection.AddTransient<ApplicationLinks>();
        }

        private class RoutingTable : IWebExecutionRequestHandlerProvider
        {
            private IServiceProvider _rootServiceProvider;
            private RequestHandlerInfo? _infoContextController_WhoAmI_897;
            private readonly static RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;

            public RoutingTable(IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_Slash(
                    pathSpan,
                    0,
                    context.Request.Method
                );
            }

            public RequestHandlerInfo? TestPath_Slash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == '/'))
                {
                    index += 1;
                    handlerInfo = TestPath_whoami(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_whoami(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 6) && (charSpan[index + 0] == 'w') && (charSpan[index + 1] == 'h') && (charSpan[index + 2] == 'o') && (charSpan[index + 3] == 'a') && (charSpan[index + 4] == 'm') && (charSpan[index + 5] == 'i'))
                {
                    index += 6;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoContextController_WhoAmI_897 ??= new RequestHandlerInfo(
                                    new ContextController_WhoAmI_897(_rootServiceProvider),
                                    PathTokenCollection.Empty
                                );
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                }
                return handlerInfo;
            }
        }
    }
}
