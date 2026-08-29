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
            private global::Test.Api.Generated.PetController_GetPet? _fieldPetController_GetPet;
            private static readonly string[] _pathTokenNamesPetController_GetPet =             new string[] { "PetId" }
;
            private static readonly global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 1) && (charSpan[index + 0] == '/'))
                {
                    index += 1;
                    handlerInfo = TestPath_petsSlash(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_petsSlash(global::System.ReadOnlySpan<char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && ((charSpan[index + 0] == 'p') || (charSpan[index + 0] == 'P')) && ((charSpan[index + 1] == 'e') || (charSpan[index + 1] == 'E')) && ((charSpan[index + 2] == 't') || (charSpan[index + 2] == 'T')) && ((charSpan[index + 3] == 's') || (charSpan[index + 3] == 'S')) && (charSpan[index + 4] == '/'))
                {
                    index += 5;
                    if (handlerInfo == null)
                    {
                        handlerInfo = TestPath_petsSlashWildCard(
                            charSpan,
                            index,
                            methodString
                        );
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_petsSlashWildCard(global::System.ReadOnlySpan<char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                handlerInfo = TestPath_NoPathWildCardMatch(
                    charSpan,
                    index,
                    methodString
                );
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_NoPathWildCardMatch(global::System.ReadOnlySpan<char> charSpan, int index, string methodString)
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
                            _fieldPetController_GetPet ??= new global::Test.Api.Generated.PetController_GetPet(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_GetPet,
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
