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
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_ListPets;
            private static readonly global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Featured;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Store;

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
                        case 'p':
                            return TestPath_ets(
                                charSpan,
                                index + 1,
                                methodString,
                                ref pathTokens
                            );
                        case 's':
                            return TestPath_tore(
                                charSpan,
                                index + 1,
                                methodString,
                                ref pathTokens
                            );
                    }
                }
                return null;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_ets(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 3) && charSpan.Slice(index, 3).SequenceEqual("ets"))
                {
                    index += 3;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_ListPets ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_ListPets(_rootServiceProvider));
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                    handlerInfo = TestPath_Slashfeatured(
                        charSpan,
                        index,
                        methodString,
                        ref pathTokens
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slashfeatured(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 9) && charSpan.Slice(index, 9).SequenceEqual("/featured"))
                {
                    index += 9;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Featured ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_Featured(_rootServiceProvider));
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_tore(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 4) && charSpan.Slice(index, 4).SequenceEqual("tore"))
                {
                    index += 4;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Store ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_Store(_rootServiceProvider));
                            default:
                                return _methodNotAllowedGETHEAD;
                        }
                    }
                }
                return handlerInfo;
            }
        }
    }
}
