using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// The two fields a handler with <c>[Output&lt;T&gt;]</c> gets: how to build the view, and the
/// assignment that makes a model mismatch a build error.
/// </summary>
/// <remarks>
/// <para>
/// The second is the interesting one. The boundary of what can be checked is not where it looks:
/// the attribute's own constraint catches a type that is not an output or has no parameterless
/// constructor, because it is bound in the final compilation where RazorBlade's output exists. What
/// nothing catches without help is that the output's <c>TModel</c> matches the handler's return
/// type - the attribute cannot express it, and the generator cannot inspect another generator's
/// output.
/// </para>
/// <para>
/// So the generator emits an assignment the compiler has to bind:
/// </para>
/// <code>
/// private static readonly IHardenedResponseOutput&lt;FortunePage&gt; _outputCheck_GetFortunes = new Views.Fortunes();
/// </code>
/// <para>
/// which only compiles if <c>Views.Fortunes : IHardenedResponseOutput&lt;FortunePage&gt;</c>. A mismatch
/// reads "cannot convert Views.Fortunes to IHardenedResponseOutput&lt;FortunePage&gt;", naming both
/// types. This is the one mechanism that works across a generator boundary: another generator's
/// output cannot be inspected, but code can be emitted that the compiler binds against it. It is
/// the same property that makes a route change break a <c>.cshtml</c> at build time.
/// </para>
/// <para>
/// It lands in generated code rather than on the attribute, so the field is named after the handler
/// to keep it traceable back to the declaration that caused it.
/// </para>
/// </remarks>
public static class OutputFactoryGenerator {

    public const string FactoryField = "_outputFactory";

    public static void Implement(RequestHandlerModel handlerModel, ClassDefinition classDefinition) {
        var output = handlerModel.ResponseInformation.OutputType;

        if (output == null) {
            return;
        }

        var factory = classDefinition.AddField(
            new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                "System",
                "Func",
                new[] { KnownTypes.Requests.IExecutionContext, KnownTypes.Requests.IHardenedResponseOutput }),
            FactoryField);

        factory.Modifiers = ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;

        // static, so the lambda is cached rather than allocated per request - it closes over
        // nothing, which is the whole reason the model is attached afterwards rather than passed in.
        factory.InitializeValue = new CodeOutputComponent(
            "static _ => new " + TypeName(output) + "()") { Indented = false };

        var model = ModelType(handlerModel);

        if (model == null) {
            return;
        }

        var check = classDefinition.AddField(
            new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                KnownTypes.Namespace.Hardened.Requests.Abstract.Outputs,
                "IHardenedResponseOutput",
                new[] { model }),
            "_outputCheck_" + handlerModel.HandlerMethod);

        check.Modifiers = ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        check.InitializeValue = new CodeOutputComponent("new " + TypeName(output) + "()") { Indented = false };
    }

    /// <summary>
    /// The type the handler actually produces, which is what a view is typed over.
    /// </summary>
    /// <remarks>
    /// <c>Task&lt;T&gt;</c> is how a value is returned rather than what it is, and the response
    /// value assigned by the generated invoke method is the awaited one - so the check has to be
    /// against <c>T</c> or it would demand a view over a task. A handler returning nothing gets no
    /// check: there is no model to match, and the output's own <c>TModel</c> is whatever it
    /// declared.
    /// </remarks>
    private static ITypeDefinition? ModelType(RequestHandlerModel handlerModel) {
        var returnType = handlerModel.ResponseInformation.ReturnType;

        if (returnType == null || returnType.Name == "void" || returnType.Name == "Void") {
            return null;
        }

        if (returnType is GenericTypeDefinition generic &&
            generic.Name is "Task" or "ValueTask" &&
            generic.TypeArguments.Count == 1) {
            return generic.TypeArguments[0];
        }

        // A bare Task has no result to render.
        return returnType.Name == "Task" ? null : returnType;
    }

    /// <summary>
    /// Global-qualified, because these files are written with
    /// <see cref="TypeOutputMode.Global"/> and carry none of the consumer's using directives.
    /// </summary>
    private static string TypeName(ITypeDefinition type) =>
        string.IsNullOrEmpty(type.Namespace) ? type.Name : "global::" + type.Namespace + "." + type.Name;
}
