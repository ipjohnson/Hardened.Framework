using DependencyModules.Runtime.Helpers;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.PathTokens;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
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
            serviceCollection.AddTransient<TestApp.Links>();
        }

        private class RoutingTable : IWebExecutionRequestHandlerProvider
        {
            private IServiceProvider _rootServiceProvider;
            private RequestHandlerInfo? _infoPetController_GetBalance;
            private RequestHandlerInfo? _infoPetController_Transfer;
            private RequestHandlerInfo? _infoPetController_Health;
            private static readonly RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;

            public RoutingTable(IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context, ref PathTokenCollection pathTokens)
            {
                if (context.Request.Headers.TryGetValue("X-Amz-Target", out var dispatchValues))
                {
                    switch (dispatchValues.ToString())
                    {
                        case "Bank.GetBalance":
                            return _infoPetController_GetBalance ??= new RequestHandlerInfo(new PetController_GetBalance(_rootServiceProvider));
                        case "Bank.Transfer":
                            return _infoPetController_Transfer ??= new RequestHandlerInfo(new PetController_Transfer(_rootServiceProvider));
                    }
                }
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_Slashhealth(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public RequestHandlerInfo? TestPath_Slashhealth(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 7) && charSpan.Slice(index, 7).SequenceEqual("/health"))
                {
                    index += 7;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Health ??= new RequestHandlerInfo(new PetController_Health(_rootServiceProvider));
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
