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
            private PetController_GetFile? _fieldPetController_GetFile;
            private readonly static string[] _pathTokenNamesPetController_GetFile =             new string[] { "path" }
;
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
                    handlerInfo = TestPath_filesSlash(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_filesSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 6) && (charSpan[index + 0] == 'f') && (charSpan[index + 1] == 'i') && (charSpan[index + 2] == 'l') && (charSpan[index + 3] == 'e') && (charSpan[index + 4] == 's') && (charSpan[index + 5] == '/'))
                {
                    index += 6;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_filesSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_filesSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        return new RequestHandlerInfo(
                            _fieldPetController_GetFile ??= new PetController_GetFile(_rootServiceProvider),
                            new PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetFile,
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
