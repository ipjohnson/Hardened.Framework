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
            private PetController_GetFlag? _fieldPetController_GetFlag;
            private readonly static string[] _pathTokenNamesPetController_GetFlag =             new string[] { "on" }
;
            private readonly static RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private PetController_GetItem? _fieldPetController_GetItem;
            private readonly static string[] _pathTokenNamesPetController_GetItem =             new string[] { "id" }
;
            private PetController_GetByKey? _fieldPetController_GetByKey;
            private readonly static string[] _pathTokenNamesPetController_GetByKey =             new string[] { "key" }
;
            private PetController_GetPrice? _fieldPetController_GetPrice;
            private readonly static string[] _pathTokenNamesPetController_GetPrice =             new string[] { "value" }
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
                        case 'f':
                            return TestPath_NoPath(
                                charSpan,
                                index + 1,
                                methodString
                            );
                        case 'i':
                            return TestPath_NoPath2(
                                charSpan,
                                index + 1,
                                methodString
                            );
                        case 'k':
                            return TestPath_NoPath4(
                                charSpan,
                                index + 1,
                                methodString
                            );
                        case 'p':
                            return TestPath_NoPath6(
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
                handlerInfo = TestPath_lagSlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_lagSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 4) && (charSpan[index + 0] == 'l') && (charSpan[index + 1] == 'a') && (charSpan[index + 2] == 'g') && (charSpan[index + 3] == '/'))
                {
                    index += 4;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_lagSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_lagSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                if (!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsBool(charSpan.Slice(index)))
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        return new RequestHandlerInfo(
                            _fieldPetController_GetFlag ??= new PetController_GetFlag(_rootServiceProvider),
                            new PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetFlag,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_NoPath2(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_temsSlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_temsSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && (charSpan[index + 0] == 't') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 'm') && (charSpan[index + 3] == 's') && (charSpan[index + 4] == '/'))
                {
                    index += 5;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_temsSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_temsSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath3(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPath3(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                        return new RequestHandlerInfo(
                            _fieldPetController_GetItem ??= new PetController_GetItem(_rootServiceProvider),
                            new PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetItem,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_NoPath4(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_eySlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_eySlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 3) && (charSpan[index + 0] == 'e') && (charSpan[index + 1] == 'y') && (charSpan[index + 2] == '/'))
                {
                    index += 3;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_eySlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_eySlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath5(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPath5(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                        return new RequestHandlerInfo(
                            _fieldPetController_GetByKey ??= new PetController_GetByKey(_rootServiceProvider),
                            new PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetByKey,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public RequestHandlerInfo? TestPath_NoPath6(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_riceSlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_riceSlash(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && (charSpan[index + 0] == 'r') && (charSpan[index + 1] == 'i') && (charSpan[index + 2] == 'c') && (charSpan[index + 3] == 'e') && (charSpan[index + 4] == '/'))
                {
                    index += 5;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_riceSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_riceSlashWildCard(ReadOnlySpan<Char> charSpan, int index, string methodString)
            {
                RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath7(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public RequestHandlerInfo? TestPath_NoPath7(ReadOnlySpan<Char> charSpan, int index, string methodString)
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
                        return new RequestHandlerInfo(
                            _fieldPetController_GetPrice ??= new PetController_GetPrice(_rootServiceProvider),
                            new PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetPrice,
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
