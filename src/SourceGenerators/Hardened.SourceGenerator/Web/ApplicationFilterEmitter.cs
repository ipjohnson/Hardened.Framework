using System.Collections.Generic;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// The filters an application declares once, for every handler in its compilation.
/// </summary>
/// <remarks>
/// <para>
/// One array beside the routing table rather than a copy appended to every handler's own metadata.
/// Three reasons, in order of weight. A handler model is a Roslyn cache key, so folding the entry
/// point's attributes into each one would make an edit to the module class invalidate every handler
/// in the application. <c>new AuditAttribute { Category = "x" }</c> written into forty metadata
/// arrays is forty places for the arguments to be spelled and forty instances in the assembly. And
/// a declaration that covers the application should appear once in the generated source, next to
/// the routing table it covers.
/// </para>
/// <para>
/// Registered as <c>IApplicationFilterDeclarations</c> and read once per handler by
/// <c>ExecutionHelper</c>, which drops any entry the handler declares nearer and merges the rest
/// into its metadata. Nothing is emitted for an entry point that declares no filter, which keeps
/// this out of the checked-in routing fixtures and off the AOT trimming surface of every
/// application that does not use it.
/// </para>
/// </remarks>
internal static class ApplicationFilterEmitter {

    public const string ContainerName = "ApplicationFilters";

    private const string DeclaredField = "_declared";

    public static void Emit(ClassDefinition appClass, IReadOnlyList<AttributeModel> declarations) {
        if (declarations.Count == 0) {
            return;
        }

        var container = appClass.AddClass(ContainerName);

        container.Modifiers |= ComponentModifier.Private | ComponentModifier.Sealed;
        container.AddBaseType(KnownTypes.Requests.IApplicationFilterDeclarations);
        container.Comment =
            "The filters this application declares for every handler in this compilation. " +
            "Merged into each handler's metadata as its chain is built; see " +
            "IApplicationFilterDeclarations.";

        var field = container.AddField(typeof(object).MakeArrayType(), DeclaredField);

        field.Modifiers |=
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        field.InitializeValue = Declared(declarations);

        var property = container.AddProperty(
            new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                "System.Collections.Generic",
                "IReadOnlyList",
                new[] { TypeDefinition.Get(typeof(object)) }),
            "Declared");

        property.Modifiers |= ComponentModifier.Public;
        property.Set = null;
        property.Get.LambdaSyntax = true;
        property.Get.AddCode(DeclaredField + ";");
    }

    /// <summary>
    /// The declarations as written, constructed once.
    /// </summary>
    /// <remarks>
    /// The same spelling <c>HandlerInfoCodeGenerator</c> emits a handler's own metadata array with,
    /// from the same <see cref="AttributeModel"/>: the constructor arguments and the property
    /// initializer an attribute was written with, already qualified.
    /// </remarks>
    private static IOutputComponent Declared(IReadOnlyList<AttributeModel> declarations) {
        var instances = new List<object>(declarations.Count);

        foreach (var declaration in declarations) {
            var instance = New(
                (ITypeDefinition)declaration.TypeDefinition,
                new CodeOutputComponent(declaration.Arguments) { Indented = false });

            if (!string.IsNullOrEmpty(declaration.PropertyAssignment)) {
                instance.AddInitValue(declaration.PropertyAssignment);
            }

            instances.Add(instance);
        }

        return NewArray(typeof(object), instances.ToArray());
    }
}
