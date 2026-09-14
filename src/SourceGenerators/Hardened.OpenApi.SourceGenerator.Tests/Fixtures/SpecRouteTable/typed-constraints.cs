using DependencyModules.Runtime.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Test.Api
{
    public partial class TestApp
    {
        [global::System.Diagnostics.CodeAnalysis.DynamicDependency(nameof(SpecRoutingTableDI))]
        private static int _openApiRoutingTableDependencies =         DependencyRegistry<TestApp>.Add(SpecRoutingTableDI)
;

        private static void SpecRoutingTableDI(global::Microsoft.Extensions.DependencyInjection.IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<
                global::Hardened.Web.Runtime.Handlers.IWebExecutionRequestHandlerProvider,
                global::Test.Api.TestApp.SpecRoutingTable
            >();
            serviceCollection.AddTransient<global::Test.Api.TestApp.Links>();
        }

        private class SpecRoutingTable : global::Hardened.Web.Runtime.Handlers.IWebExecutionRequestHandlerProvider
        {
            private global::System.IServiceProvider _rootServiceProvider;
            private static readonly string[] _pathTokenNamesPetController_GetFlag =             new string[] { "on" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_GetFlag;
            private static readonly global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private static readonly string[] _pathTokenNamesPetController_GetItem =             new string[] { "id" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_GetItem;
            private static readonly string[] _pathTokenNamesPetController_GetByKey =             new string[] { "key" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_GetByKey;
            private static readonly string[] _pathTokenNamesPetController_GetPrice =             new string[] { "value" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_GetPrice;

            public SpecRoutingTable(global::System.IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? GetExecutionRequestHandler(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_Slash(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_SlashCaseStatement(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_lagSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_lagSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPathWildCardMatch(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPathWildCardMatch(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_GetFlag,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetFlag ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_GetFlag(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_temsSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_temsSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath2(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath2(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_GetItem,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetItem ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_GetItem(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_eySlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_eySlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath3(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath3(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_GetByKey,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetByKey ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_GetByKey(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_riceSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_riceSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPath4(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPath4(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_GetPrice,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetPrice ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_GetPrice(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
