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
            private static readonly string[] _pathTokenNamesPetController_GetFile =             new string[] { "path" }
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_GetFile;
            private static readonly global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;

            public SpecRoutingTable(global::System.IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? GetExecutionRequestHandler(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_SlashfilesSlash(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_SlashfilesSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 7) && charSpan.Slice(index, 7).SequenceEqual("/files/"))
                {
                    index += 7;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_filesSlashWildCard(
                            charSpan,
                            index,
                            methodString,
                            ref pathTokens
                        );
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_filesSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
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
                switch (methodString)
                {
                    case "HEAD":
                    case "GET":
                        pathTokens = new global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection(
                            _pathTokenNamesPetController_GetFile,
                            charSpan.Slice(index).ToString()
                        );
                        return _infoPetController_GetFile ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_GetFile(_rootServiceProvider));
                    default:
                        return _methodNotAllowedGETHEAD;
                }
            }
        }
    }
}
