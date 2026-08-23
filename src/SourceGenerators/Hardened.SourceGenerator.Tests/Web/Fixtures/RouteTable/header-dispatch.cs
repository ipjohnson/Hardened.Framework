using DependencyModules.Runtime.Helpers;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using Test.Api;
using Test.Api.Generated;
using Test.Api.Services;

namespace Test.Api
{
    public partial class TestApp
    {
        [DynamicDependency(nameof(RoutingTableDI))]
        private static int _routingTableDependencies =         DependencyRegistry<TestApp>.Add(RoutingTableDI)
;

        private static void RoutingTableDI(IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<
                IWebExecutionRequestHandlerProvider,
                TestApp.RoutingTable
            >();
            serviceCollection.AddTransient<IPetService>();
            serviceCollection.AddTransient<TestAppLinks>();
        }

        private class RoutingTable : IWebExecutionRequestHandlerProvider
        {
            private IServiceProvider _rootServiceProvider;
            private RequestHandlerInfo? _infoPetController_GetBalance;
            private RequestHandlerInfo? _infoPetController_Transfer;
            private readonly static RequestHandlerInfo _methodNotAllowedPOST =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("POST")
;
            private RequestHandlerInfo? _infoPetController_Health;
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
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "POST":
                                return _infoPetController_GetBalance ??= new RequestHandlerInfo(
                                    new PetController_GetBalance(_rootServiceProvider),
                                    PathTokenCollection.Empty
                                );
                            case "POST":
                                return _infoPetController_Transfer ??= new RequestHandlerInfo(
                                    new PetController_Transfer(_rootServiceProvider),
                                    PathTokenCollection.Empty
                                );
                            default:
                                return _methodNotAllowedPOST;
                        }
                    }
                    handlerInfo = TestPath_h(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_h(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == 'h'))
                {
                    index += 1;
                    handlerInfo = TestPath_ealth(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_ealth(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && (charSpan[index + 0] == 'e') && (charSpan[index + 1] == 'a') && (charSpan[index + 2] == 'l') && (charSpan[index + 3] == 't') && (charSpan[index + 4] == 'h'))
                {
                    index += 5;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Health ??= new RequestHandlerInfo(
                                    new PetController_Health(_rootServiceProvider),
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
