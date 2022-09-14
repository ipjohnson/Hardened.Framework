using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.SourceGenerator.Shared
{
    public  static class ApplicationRootImplementation
    {
        public static InstanceDefinition ImplementApplicationRoot(this ClassDefinition appClass)
        {
            appClass.AddBaseType(KnownTypes.Application.IApplicationRoot);

            var rootService = appClass.AddField(KnownTypes.DI.ServiceProvider, "RootServiceProvider");

            var provider = appClass.AddProperty(KnownTypes.DI.IServiceProvider, "Provider");

            provider.Get.LambdaSyntax = true;
            provider.Get.AddCode("RootServiceProvider ?? throw new Exception(\"RootServiceProvider not initialized yet\");");
            provider.Set = null;

            var disposeAsync = appClass.AddMethod("DisposeAsync");

            disposeAsync.Modifiers = ComponentModifier.Public | ComponentModifier.Async;
            disposeAsync.SetReturnType(typeof(ValueTask));

            var invokeStatement = Await(rootService.Instance.Invoke("DisposeAsync"));

            disposeAsync.If("RootServiceProvider != null").AddIndentedStatement(invokeStatement);

            return rootService.Instance;
        }
    }
}
