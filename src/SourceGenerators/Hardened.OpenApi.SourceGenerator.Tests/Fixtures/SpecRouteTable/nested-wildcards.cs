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
            private static readonly string[] _pathTokenNamesPetController_ThreeTokens =             new string[] { "x", "y", "z" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_ThreeTokens;
            private static readonly global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private static readonly string[] _pathTokenNamesPetController_TwoTokens =             new string[] { "x", "y" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_TwoTokens;

            public SpecRoutingTable(global::System.IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? GetExecutionRequestHandler(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_SlashaSlash(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_SlashaSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_aSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_SlashWildCardMatch(
                    charSpan,
                    index,
                    methodString,
                    ref pathTokens
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_SlashWildCardMatch(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                var handlerInfo = (global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo?)null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_bSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_bSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slash2(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                var handlerInfo = (global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo?)null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_cSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_cSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_ThreeTokens,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_ThreeTokens ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_ThreeTokens(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
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
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_TwoTokens,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_TwoTokens ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_TwoTokens(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
