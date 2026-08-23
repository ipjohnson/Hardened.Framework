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
            private global::Test.Api.Generated.PetController_ThreeTokens? _fieldPetController_ThreeTokens;
            private readonly static global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private global::Test.Api.Generated.PetController_TwoTokens? _fieldPetController_TwoTokens;

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
                    handlerInfo = TestPath_aSlash(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_aSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 2) && (charSpan[index + 0] == 'a') && (charSpan[index + 1] == '/'))
                {
                    index += 2;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_aSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_aSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_SlashWildCardMatch(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_SlashWildCardMatch(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                var handlerInfo = (global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo?)null;
                var currentIndex = index;
                var segmentEnd = charSpan.Slice(index).IndexOf('/');
                var segmentLimit = segmentEnd < 0 ? charSpan.Length : index + segmentEnd + 1;
                while((currentIndex < segmentLimit))
                {
                    if (currentIndex > index && (charSpan.Length >= currentIndex + 1) && (charSpan[currentIndex + 0] == '/'))
                    {
                        handlerInfo = TestPath_bSlash(
                            charSpan,
                            (currentIndex + 1),
                            methodString
                        );
                        if (handlerInfo != null)
                        {
                            handlerInfo.PathTokens.Set(
                                0,
                                new global::Hardened.Requests.Abstract.PathTokens.PathToken(
                                    "x",
                                    charSpan.Slice(
                                        index,
                                        (currentIndex - index)
                                    ).ToString()
                                )
                            );
                            return handlerInfo;
                        }
                    }
                    currentIndex++;
                }
                return null;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_bSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 2) && (charSpan[index + 0] == 'b') && (charSpan[index + 1] == '/'))
                {
                    index += 2;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_bSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_bSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_Slash2(
                    charSpan,
                    index,
                    methodString
                );
                if (handlerInfo == null)
                {
                    handlerInfo = TestPath_NoPath2(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slash2(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                var handlerInfo = (global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo?)null;
                var currentIndex = index;
                var segmentEnd = charSpan.Slice(index).IndexOf('/');
                var segmentLimit = segmentEnd < 0 ? charSpan.Length : index + segmentEnd + 1;
                while((currentIndex < segmentLimit))
                {
                    if (currentIndex > index && (charSpan.Length >= currentIndex + 1) && (charSpan[currentIndex + 0] == '/'))
                    {
                        handlerInfo = TestPath_cSlash(
                            charSpan,
                            (currentIndex + 1),
                            methodString
                        );
                        if (handlerInfo != null)
                        {
                            handlerInfo.PathTokens.Set(
                                1,
                                new global::Hardened.Requests.Abstract.PathTokens.PathToken(
                                    "y",
                                    charSpan.Slice(
                                        index,
                                        (currentIndex - index)
                                    ).ToString()
                                )
                            );
                            return handlerInfo;
                        }
                    }
                    currentIndex++;
                }
                return null;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_cSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 2) && (charSpan[index + 0] == 'c') && (charSpan[index + 1] == '/'))
                {
                    index += 2;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_cSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_cSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPathWildCardMatch(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPathWildCardMatch(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_ThreeTokens ??= new global::Test.Api.Generated.PetController_ThreeTokens(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                3,
                                new global::Hardened.Requests.Abstract.PathTokens.PathToken(
                                    "z",
                                    charSpan.Slice(index).ToString()
                                )
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath2(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_TwoTokens ??= new global::Test.Api.Generated.PetController_TwoTokens(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                2,
                                new global::Hardened.Requests.Abstract.PathTokens.PathToken(
                                    "y",
                                    charSpan.Slice(index).ToString()
                                )
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
