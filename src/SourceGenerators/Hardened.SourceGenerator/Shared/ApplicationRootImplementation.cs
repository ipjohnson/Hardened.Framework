using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.SourceGenerator.Shared;

public static class ApplicationRootImplementation {
    public static InstanceDefinition ImplementApplicationRoot(this ClassDefinition appClass) {
        appClass.AddBaseType(KnownTypes.Application.IApplicationRoot);

        var rootService = appClass.AddField(KnownTypes.DI.ServiceProvider, "RootServiceProvider");

        var provider = appClass.AddProperty(KnownTypes.DI.IServiceProvider, "Provider");

        provider.Get.LambdaSyntax = true;
        // {arg1} keeps Exception a type until the file is serialized. Spelled into the string it
        // was text, and resolved only where something else had already imported System.
        provider.Get.AddCode(
            "RootServiceProvider ?? throw new {arg1}(\"RootServiceProvider not initialized yet\");",
            typeof(Exception));
        provider.Set = null;

        var disposeAsync = appClass.AddMethod("DisposeAsync");

        disposeAsync.Modifiers = ComponentModifier.Public | ComponentModifier.Async;
        disposeAsync.SetReturnType(typeof(ValueTask));

        var currentRootServiceProvider =
            disposeAsync.Assign("RootServiceProvider").ToVar("currentRootServiceProvider");

        var invokeStatement = Await(currentRootServiceProvider.Invoke("DisposeAsync"));

        var ifBlock = disposeAsync.If("RootServiceProvider != null");

        // null!, not null: the field is declared non-nullable but is genuinely nullable - the
        // Provider getter above is "RootServiceProvider ?? throw". Emitting a bare null made every
        // generated application root carry a CS8625, which fails CI in any consumer building with
        // TreatWarningsAsErrors. Hardened.Amz's LambdaWebTest was doing exactly that.
        ifBlock.AddIndentedStatement("RootServiceProvider = null!");
        ifBlock.AddIndentedStatement(invokeStatement);

        return rootService.Instance;
    }
}