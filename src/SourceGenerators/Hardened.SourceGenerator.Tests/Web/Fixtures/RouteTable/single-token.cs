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
            private static readonly string[] _pathTokenNamesPetController_GetPet =             new string[] { "petId" }
;
            private RequestHandlerInfo? _infoPetController_GetPet;
            private static readonly RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;

            public RoutingTable(IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context, ref PathTokenCollection pathTokens)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_SlashpetsSlash(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public RequestHandlerInfo? TestPath_SlashpetsSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 6) && charSpan.Slice(index, 6).SequenceEqual("/pets/"))
                {
                    index += 6;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_petsSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_petsSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPathWildCardMatch(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPathWildCardMatch(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
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
                        pathTokens = new PathTokenCollection(
                            _pathTokenNamesPetController_GetPet,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetPet ??= new RequestHandlerInfo(new PetController_GetPet(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
