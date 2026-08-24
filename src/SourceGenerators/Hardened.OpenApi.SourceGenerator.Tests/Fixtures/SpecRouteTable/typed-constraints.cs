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
            private global::Test.Api.Generated.PetController_GetFlag? _fieldPetController_GetFlag;
            private readonly static string[] _pathTokenNamesPetController_GetFlag =             new string[] { "on" }
;
            private readonly static global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private global::Test.Api.Generated.PetController_GetItem? _fieldPetController_GetItem;
            private readonly static string[] _pathTokenNamesPetController_GetItem =             new string[] { "id" }
;
            private global::Test.Api.Generated.PetController_GetByKey? _fieldPetController_GetByKey;
            private readonly static string[] _pathTokenNamesPetController_GetByKey =             new string[] { "key" }
;
            private global::Test.Api.Generated.PetController_GetPrice? _fieldPetController_GetPrice;
            private readonly static string[] _pathTokenNamesPetController_GetPrice =             new string[] { "value" }
;

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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_lagSlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_lagSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_lagSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                if (!global::Hardened.Web.Runtime.Routing.RouteConstraints.IsBool(charSpan.Slice(index)))
                {
                    return null;
                }
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_GetFlag ??= new global::Test.Api.Generated.PetController_GetFlag(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetFlag,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath2(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_temsSlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_temsSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_temsSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath3(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath3(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_GetItem ??= new global::Test.Api.Generated.PetController_GetItem(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetItem,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath4(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_eySlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_eySlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_eySlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath5(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath5(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_GetByKey ??= new global::Test.Api.Generated.PetController_GetByKey(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetByKey,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath6(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_riceSlash(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_riceSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_riceSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath7(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath7(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_GetPrice ??= new global::Test.Api.Generated.PetController_GetPrice(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
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
