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
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_GetBalance;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Transfer;
            private global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? _infoPetController_Health;
            private static readonly global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedGETHEAD =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("GET, HEAD")
;

            public SpecRoutingTable(global::System.IServiceProvider serviceProvider)
            {
                _rootServiceProvider = serviceProvider;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? GetExecutionRequestHandler(global::Hardened.Requests.Abstract.Execution.IExecutionContext context, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                if (context.Request.Headers.TryGetValue("X-Amz-Target", out var dispatchValues))
                {
                    switch (dispatchValues.ToString())
                    {
                        case "Bank.GetBalance":
                            return _infoPetController_GetBalance ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_GetBalance(_rootServiceProvider));
                        case "Bank.Transfer":
                            return _infoPetController_Transfer ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_Transfer(_rootServiceProvider));
                    }
                }
                var pathSpan = context.Request.Path.AsSpan();
                return TestPath_Slashhealth(
                    pathSpan,
                    0,
                    context.Request.Method,
                    ref pathTokens
                );
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_Slashhealth(global::System.ReadOnlySpan<char> charSpan, int index, string methodString, ref global::Hardened.Requests.Abstract.PathTokens.PathTokenCollection pathTokens)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 7) && charSpan.Slice(index, 7).SequenceEqual("/health"))
                {
                    index += 7;
                    if (charSpan.Length == index)
                    {
                        switch (methodString)
                        {
                            case "HEAD":
                            case "GET":
                                return _infoPetController_Health ??= new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(new global::Test.Api.Generated.PetController_Health(_rootServiceProvider));
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
