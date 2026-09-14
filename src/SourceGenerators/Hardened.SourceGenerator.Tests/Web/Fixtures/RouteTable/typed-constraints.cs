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
            private static readonly string[] _pathTokenNamesPetController_GetFlag =             new string[] { "on" }
;
            private RequestHandlerInfo? _infoPetController_GetFlag;
            private static readonly RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private static readonly string[] _pathTokenNamesPetController_GetItem =             new string[] { "id" }
;
            private RequestHandlerInfo? _infoPetController_GetItem;
            private static readonly string[] _pathTokenNamesPetController_GetByKey =             new string[] { "key" }
;
            private RequestHandlerInfo? _infoPetController_GetByKey;
            private static readonly string[] _pathTokenNamesPetController_GetPrice =             new string[] { "value" }
;
            private RequestHandlerInfo? _infoPetController_GetPrice;

            public RoutingTable(IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public RequestHandlerInfo? GetExecutionRequestHandler(IExecutionContext context, ref PathTokenCollection pathTokens)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_Slash(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public RequestHandlerInfo? TestPath_Slash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == '/'))
                {
                    index += 1;
                    handlerInfo = TestPath_SlashCaseStatement(
                        charSpan,
                        index,
                        methodString,
                        ref pathTokens
                    );
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_SlashCaseStatement(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                if (charSpan.Length > index)
                {
                    switch (charSpan[index])
                    {
                        case 'f':
                            return TestPath_lagSlash(
                                charSpan,
                                index + 1,
                                methodString,
                                ref pathTokens
                            );
                        case 'i':
                            return TestPath_temsSlash(
                                charSpan,
                                index + 1,
                                methodString,
                                ref pathTokens
                            );
                        case 'k':
                            return TestPath_eySlash(
                                charSpan,
                                index + 1,
                                methodString,
                                ref pathTokens
                            );
                        case 'p':
                            return TestPath_riceSlash(
                                charSpan,
                                index + 1,
                                methodString,
                                ref pathTokens
                            );
                    }
                }
                return null;
            }

            public RequestHandlerInfo? TestPath_lagSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 4) && charSpan.Slice(index, 4).SequenceEqual("lag/"))
                {
                    index += 4;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_lagSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_lagSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
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
                if (!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsBool(charSpan.Slice(index)))
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new PathTokenCollection(
                            _pathTokenNamesPetController_GetFlag,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetFlag ??= new RequestHandlerInfo(new PetController_GetFlag(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_temsSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && charSpan.Slice(index, 5).SequenceEqual("tems/"))
                {
                    index += 5;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_temsSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_temsSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath2(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
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
                if (!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsInt(charSpan.Slice(index)))
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new PathTokenCollection(
                            _pathTokenNamesPetController_GetItem,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetItem ??= new RequestHandlerInfo(new PetController_GetItem(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_eySlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 3) && charSpan.Slice(index, 3).SequenceEqual("ey/"))
                {
                    index += 3;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_eySlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_eySlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath3(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPath3(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                if (charSpan.Length <= index)
                {
                    return null;
                }
                if (charSpan.Slice(index).IndexOf('/') >= 0)
                {
                    return null;
                }
                if (!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsGuid(charSpan.Slice(index)))
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new PathTokenCollection(
                            _pathTokenNamesPetController_GetByKey,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetByKey ??= new RequestHandlerInfo(new PetController_GetByKey(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_riceSlash(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && charSpan.Slice(index, 5).SequenceEqual("rice/"))
                {
                    index += 5;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_riceSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_riceSlashWildCard(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath4(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPath4(ReadOnlySpan<char> charSpan, int index, string methodString, ref PathTokenCollection pathTokens)
            {
                if (charSpan.Length <= index)
                {
                    return null;
                }
                if (charSpan.Slice(index).IndexOf('/') >= 0)
                {
                    return null;
                }
                if (!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsDecimal(charSpan.Slice(index)))
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new PathTokenCollection(
                            _pathTokenNamesPetController_GetPrice,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetPrice ??= new RequestHandlerInfo(new PetController_GetPrice(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
