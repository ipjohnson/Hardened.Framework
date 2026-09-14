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
            private static readonly string[] _pathTokenNamesPetController_ThreeTokens =             new string[] { "x", "y", "z" }
;
            private RequestHandlerInfo? _infoPetController_ThreeTokens;
            private static readonly RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private static readonly string[] _pathTokenNamesPetController_TwoTokens =             new string[] { "x", "y" }
;
            private RequestHandlerInfo? _infoPetController_TwoTokens;

            public RoutingTable(IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context, ref PathTokenCollection pathTokens)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_SlashaSlash(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public RequestHandlerInfo? TestPath_SlashaSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 3) && charSpan.Slice(index, 3).SequenceEqual("/a/"))
                {
                    index += 3;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_aSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_aSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_SlashWildCardMatch(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_SlashWildCardMatch(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                var handlerInfo = (RequestHandlerInfo?)null;
                var segmentEnd = charSpan.Slice(index).IndexOf('/');
                var segmentLimit = segmentEnd < 0 ? charSpan.Length : index + segmentEnd + 1;
                var currentIndex = segmentEnd < 0 ? segmentLimit : index + segmentEnd;
                while ((currentIndex < segmentLimit))
                {
                    if (currentIndex > index && (charSpan.Length >= currentIndex + 1) && (charSpan[currentIndex + 0] == '/'))
                    {
                        handlerInfo = TestPath_bSlash(
                            charSpan,
                            (currentIndex + 1),
                            methodString,
                            ref pathTokens
                        );
                        if (handlerInfo != null)
                        {
                            if (handlerInfo.Handler != null)
                            {
                                pathTokens.SetValue(
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

            public RequestHandlerInfo? TestPath_bSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 2) && charSpan.Slice(index, 2).SequenceEqual("b/"))
                {
                    index += 2;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_bSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_bSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_Slash2(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                if (handlerInfo == null)
                {
                    handlerInfo = TestPath_NoPath2(
                        charSpan,
                        index,
                        methodString,
                        ref pathTokens
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_Slash2(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                var handlerInfo = (RequestHandlerInfo?)null;
                var segmentEnd = charSpan.Slice(index).IndexOf('/');
                var segmentLimit = segmentEnd < 0 ? charSpan.Length : index + segmentEnd + 1;
                var currentIndex = segmentEnd < 0 ? segmentLimit : index + segmentEnd;
                while ((currentIndex < segmentLimit))
                {
                    if (currentIndex > index && (charSpan.Length >= currentIndex + 1) && (charSpan[currentIndex + 0] == '/'))
                    {
                        handlerInfo = TestPath_cSlash(
                            charSpan,
                            (currentIndex + 1),
                            methodString,
                            ref pathTokens
                        );
                        if (handlerInfo != null)
                        {
                            if (handlerInfo.Handler != null)
                            {
                                pathTokens.SetValue(
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

            public RequestHandlerInfo? TestPath_cSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 2) && charSpan.Slice(index, 2).SequenceEqual("c/"))
                {
                    index += 2;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_cSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_cSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
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
                            _pathTokenNamesPetController_ThreeTokens,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_ThreeTokens ??= new RequestHandlerInfo(new PetController_ThreeTokens(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_NoPath2(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
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
                            _pathTokenNamesPetController_TwoTokens,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_TwoTokens ??= new RequestHandlerInfo(new PetController_TwoTokens(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
