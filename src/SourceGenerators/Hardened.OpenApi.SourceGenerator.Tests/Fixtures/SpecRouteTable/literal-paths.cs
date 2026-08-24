using DependencyModules.Runtime.Helpers;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Handlers;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Diagnostics.CodeAnalysis;

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
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_ListPets;
            private readonly static global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Featured;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Store;

            public SpecRoutingTable(global::System.IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? GetExecutionRequestHandler(global::Hardened.Requests.Abstract.Execution.IExecutionContext context)
            {
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
                    handlerInfo = TestPath_SlashCaseStatement(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_SlashCaseStatement(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_ets(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_ets(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 3) && (charSpan[index + 0] == 'e') && (charSpan[index + 1] == 't') && (charSpan[index + 2] == 's'))
                {
                    index += 3;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_ListPets ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                                    new global::Test.Api.Generated.PetController_ListPets(_rootServiceProvider),
                                    global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection.Empty
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slash2(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_featured(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 8) && (charSpan[index + 0] == 'f') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 'a') && (charSpan[index + 3] == 't') && (charSpan[index + 4] == 'u') && (charSpan[index + 5] == 'r') && (charSpan[index + 6] == 'e') && (charSpan[index + 7] == 'd'))
                {
                    index += 8;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Featured ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                                    new global::Test.Api.Generated.PetController_Featured(_rootServiceProvider),
                                    global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection.Empty
                                );
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath2(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_tore(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_tore(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 4) && (charSpan[index + 0] == 't') && (charSpan[index + 1] == 'o') && (charSpan[index + 2] == 'r') && (charSpan[index + 3] == 'e'))
                {
                    index += 4;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Store ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                                    new global::Test.Api.Generated.PetController_Store(_rootServiceProvider),
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
