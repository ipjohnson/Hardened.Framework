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
            serviceCollection.AddTransient<TestApp.Links>();
        }

        private class RoutingTable : IWebExecutionRequestHandlerProvider
        {
            private IServiceProvider _rootServiceProvider;
            private RequestHandlerInfo? _infoPetController_ListPets;
            private readonly static RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private RequestHandlerInfo? _infoPetController_Featured;
            private RequestHandlerInfo? _infoPetController_Store;

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
                    handlerInfo = TestPath_SlashCaseStatement(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_SlashCaseStatement(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                if (charSpan.Length > index)
                {
                    switch (charSpan[index])
                    {
                        case 'p':
                            return TestPath_NoPath(
                                charSpan,
                                index + 1,
                                methodString
                            );
                        case 's':
                            return TestPath_NoPath2(
                                charSpan,
                                index + 1,
                                methodString
                            );
                    }
                }
                return null;
            }

            public RequestHandlerInfo? TestPath_NoPath(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_ets(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_ets(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 3) && (charSpan[index + 0] == 'e') && (charSpan[index + 1] == 't') && (charSpan[index + 2] == 's'))
                {
                    index += 3;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_ListPets ??= new RequestHandlerInfo(
                                    new PetController_ListPets(_rootServiceProvider),
                                    PathTokenCollection.Empty
                                );
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                    handlerInfo = TestPath_Slash2(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_Slash2(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == '/'))
                {
                    index += 1;
                    handlerInfo = TestPath_featured(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_featured(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 8) && (charSpan[index + 0] == 'f') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 'a') && (charSpan[index + 3] == 't') && (charSpan[index + 4] == 'u') && (charSpan[index + 5] == 'r') && (charSpan[index + 6] == 'e') && (charSpan[index + 7] == 'd'))
                {
                    index += 8;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Featured ??= new RequestHandlerInfo(
                                    new PetController_Featured(_rootServiceProvider),
                                    PathTokenCollection.Empty
                                );
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPath2(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_tore(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_tore(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 4) && (charSpan[index + 0] == 't') && (charSpan[index + 1] == 'o') && (charSpan[index + 2] == 'r') && (charSpan[index + 3] == 'e'))
                {
                    index += 4;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Store ??= new RequestHandlerInfo(
                                    new PetController_Store(_rootServiceProvider),
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
