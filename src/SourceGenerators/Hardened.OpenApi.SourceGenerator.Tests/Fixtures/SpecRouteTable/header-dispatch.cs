using DependencyModules.Runtime.Helpers;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;
using Test.Api.Generated;

namespace Test.Api
{
    public partial class TestApp
    {
        [DynamicDependency(nameof(SpecRoutingTableDI))]
        private static int _openApiRoutingTableDependencies =         DependencyRegistry<TestApp>.Add(SpecRoutingTableDI)
;

        private static void SpecRoutingTableDI(global::Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<
                global::Hardened.Web.Runtime.Handlers.IWebExecutionRequestHandlerProvider,
                global::Test.Api.TestApp.SpecRoutingTable
            >();
            serviceCollection.AddTransient<global::Test.Api.TestAppLinks>();
        }

        private class SpecRoutingTable : global::Hardened.Web.Runtime.Handlers.IWebExecutionRequestHandlerProvider
        {
            private global::System.IServiceProvider _rootServiceProvider;
            private global::Test.Api.Generated.PetController_GetBalance? _fieldPetController_GetBalance;
            private global::Test.Api.Generated.PetController_Transfer? _fieldPetController_Transfer;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Health;
            private readonly static global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;

            public SpecRoutingTable(global::System.IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? GetExecutionRequestHandler(global::Hardened.Requests.Abstract.Execution.IExecutionContext context)
            {
                if (context.Request.Headers.TryGetValue("X-Amz-Target", out var dispatchValues))
                {
                    switch (dispatchValues.ToString())
                    {
                        case "Bank.GetBalance":
                            return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                                _fieldPetController_GetBalance ??= new global::Test.Api.Generated.PetController_GetBalance(_rootServiceProvider),
                                global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection.Empty
                            );
                        case "Bank.Transfer":
                            return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                                _fieldPetController_Transfer ??= new global::Test.Api.Generated.PetController_Transfer(_rootServiceProvider),
                                global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection.Empty
                            );
                    }
                }
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_Slash(
                    pathSpan,
                    0,
                    context.Request.Method
                );
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == '/'))
                {
                    index += 1;
                    handlerInfo = TestPath_health(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_health(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 6) && (charSpan[index + 0] == 'h') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 'a') && (charSpan[index + 3] == 'l') && (charSpan[index + 4] == 't') && (charSpan[index + 5] == 'h'))
                {
                    index += 6;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Health ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                                    new global::Test.Api.Generated.PetController_Health(_rootServiceProvider),
                                    global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection.Empty
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
