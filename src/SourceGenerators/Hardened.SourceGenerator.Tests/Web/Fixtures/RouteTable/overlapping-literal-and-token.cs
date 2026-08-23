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
            private RequestHandlerInfo? _infoPetController_Special;
            private readonly static RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private PetController_GetPet? _fieldPetController_GetPet;
            private readonly static string[] _pathTokenNamesPetController_GetPet =             new string[] { "petId" }
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
                    handlerInfo = TestPath_petsSlash(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_petsSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && (charSpan[index + 0] == 'p') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 't') && (charSpan[index + 3] == 's') && (charSpan[index + 4] == '/'))
                {
                    index += 5;
                    handlerInfo = TestPath_s(
                        charSpan,
                        index,
                        methodString
                    );
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_petsSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_s(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == 's'))
                {
                    index += 1;
                    handlerInfo = TestPath_pecial(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_pecial(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 6) && (charSpan[index + 0] == 'p') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 'c') && (charSpan[index + 3] == 'i') && (charSpan[index + 4] == 'a') && (charSpan[index + 5] == 'l'))
                {
                    index += 6;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Special ??= new RequestHandlerInfo(
                                    new PetController_Special(_rootServiceProvider),
                                    PathTokenCollection.Empty
                                );
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_petsSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPathWildCardMatch(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPathWildCardMatch(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                if (charSpan.Length <= index)
                {
                    return null;
                }
                if (charSpan.Slice(index).IndexOf('/') >= 0)
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        return new RequestHandlerInfo(
                            _fieldPetController_GetPet ??= new PetController_GetPet(_rootServiceProvider),
                            new PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetPet,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
