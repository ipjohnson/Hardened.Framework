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
            private global::Test.Api.Generated.PetController_GetPet? _fieldPetController_GetPet;
            private readonly static string[] _pathTokenNamesPetController_GetPet =             new string[] { "petId" }
;
            private global::Test.Api.Generated.PetController_UpdatePet? _fieldPetController_UpdatePet;
            private readonly static string[] _pathTokenNamesPetController_UpdatePet =             new string[] { "petId" }
;
            private global::Test.Api.Generated.PetController_DeletePet? _fieldPetController_DeletePet;
            private readonly static string[] _pathTokenNamesPetController_DeletePet =             new string[] { "petId" }
;
            private readonly static global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo _methodNotAllowedDELETEGETHEADPUT =             global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo.MethodNotAllowed("DELETE, GET, HEAD, PUT")
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
                    handlerInfo = TestPath_petsSlash(
                        charSpan,
                        index,
                        methodString
                    );
                }
                return handlerInfo;
            }

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_petsSlash(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
            {
                global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? handlerInfo = null;
                if ((charSpan.Length >= index + 5) && (charSpan[index + 0] == 'p') && (charSpan[index + 1] == 'e') && (charSpan[index + 2] == 't') && (charSpan[index + 3] == 's') && (charSpan[index + 4] == '/'))
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

            public global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo? TestPath_petsSlashWildCard(global::System.ReadOnlySpan<global::System.Char> charSpan, int index, string methodString)
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
                    case "PUT":
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_UpdatePet ??= new global::Test.Api.Generated.PetController_UpdatePet(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_UpdatePet,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    case "DELETE":
                        return new global::Hardened.Web.Runtime.Handlers.RequestHandlerInfo(
                            _fieldPetController_DeletePet ??= new global::Test.Api.Generated.PetController_DeletePet(_rootServiceProvider),
                            new global::Hardened.Requests.Runtime.PathTokens.PathTokenCollection(
                                1,
                                _pathTokenNamesPetController_DeletePet,
                                charSpan.Slice(index).ToString()
                            )
                        );
                    default:
                        return _methodNotAllowedDELETEGETHEADPUT;
                }
            }
        }
    }
}
