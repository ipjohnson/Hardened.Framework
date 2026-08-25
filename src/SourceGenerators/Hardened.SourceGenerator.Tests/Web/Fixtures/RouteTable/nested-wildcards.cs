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
            private PetController_ThreeTokens? _fieldPetController_ThreeTokens;
            private readonly static string[] _pathTokenNamesPetController_ThreeTokens =             new string[] { "x", "y", "z" }
;
            private readonly static RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private PetController_TwoTokens? _fieldPetController_TwoTokens;
            private readonly static string[] _pathTokenNamesPetController_TwoTokens =             new string[] { "x", "y" }
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
                    handlerInfo = TestPath_aSlash(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_aSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
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

            public RequestHandlerInfo? TestPath_aSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_SlashWildCardMatch(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_SlashWildCardMatch(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                var handlerInfo = (RequestHandlerInfo?)null;
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
                            if (handlerInfo.Handler != null)
                            {
                                handlerInfo.PathTokens.SetValue(
                                    0,
                                    charSpan.Slice(
                                        index,
                                        (currentIndex - index)
                                    ).ToString()
                                );
                            }
                            return handlerInfo;
                        }
                    }
                    currentIndex++;
                }
                return null;
            }

            public RequestHandlerInfo? TestPath_bSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
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

            public RequestHandlerInfo? TestPath_bSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
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

            public RequestHandlerInfo? TestPath_Slash2(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                var handlerInfo = (RequestHandlerInfo?)null;
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
                            if (handlerInfo.Handler != null)
                            {
                                handlerInfo.PathTokens.SetValue(
                                    1,
                                    charSpan.Slice(
                                        index,
                                        (currentIndex - index)
                                    ).ToString()
                                );
                            }
                            return handlerInfo;
                        }
                    }
                    currentIndex++;
                }
                return null;
            }

            public RequestHandlerInfo? TestPath_cSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
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

            public RequestHandlerInfo? TestPath_cSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                            _fieldPetController_ThreeTokens ??= new PetController_ThreeTokens(_rootServiceProvider),
                            new PathTokenCollection(
                                3,
                                _pathTokenNamesPetController_ThreeTokens,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_NoPath2(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                            _fieldPetController_TwoTokens ??= new PetController_TwoTokens(_rootServiceProvider),
                            new PathTokenCollection(
                                2,
                                _pathTokenNamesPetController_TwoTokens,
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
