using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Generation;

namespace Hardened.Idl.Emitters;

/// <summary>
/// The exception an implementation throws to produce a response the specification declares, where
/// the framework ships no type for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One type per declared error, not per operation and status.</b> It used to be the second:
/// <c>GetPetNotFoundException</c> beside <c>GetPetLabelNotFoundException</c> - same base, same
/// status, same payload type, same body accessor, differing only in which operation the author was
/// looking at. Nothing downstream read either type's identity, because a generated exception
/// carries exactly two facts and both are baked into its constructor.
/// </para>
/// <para>
/// <b>And most declared errors get no type here at all.</b> The rule the operation prefix existed
/// to serve - two responses in one set must not resolve to one C# type - is solved by the per-status
/// wrapper, and <c>Hardened.Requests.Abstract.Responses</c> already ships those. A declared 404 with
/// a <c>Problem</c> is thrown as <c>new NotFound&lt;Problem&gt;(problem).AsException()</c>, which is
/// the same record a code-first handler returns. <see cref="ShippedResponses.For"/> is the one
/// decision, and what reaches this emitter is what it declined to bind: an error the description
/// named, or a status registered nowhere.
/// </para>
/// <para>
/// The signature is unchanged either way: <c>Task&lt;Pet&gt; GetPet(string petId)</c> still returns
/// a pet, and the declared 404 arrives by being thrown. That is what makes throws mode
/// non-breaking - expressing error responses in the return type would rewrite every existing
/// signature, for a case most specifications do not have.
/// </para>
/// </remarks>
internal static class ErrorResponseEmitter {

    /// <param name="errors">
    /// The distinct errors that need a generated exception, one entry each. Computed by
    /// <see cref="SpecFileEmitter"/> across the whole document rather than walked per operation
    /// here, because two operations declaring one error want one type - which is the entire point
    /// of the change - and two <em>services</em> declaring it would otherwise emit the same class
    /// twice into one namespace.
    /// </param>
    public static IReadOnlyList<ClassDefinition> Emit(
        IConstructContainer container, IReadOnlyList<ErrorResponseModel> errors,
        string modelsNamespace) {
        var emitted = new List<ClassDefinition>();

        foreach (var error in errors) {
            emitted.Add(EmitException(container, error, modelsNamespace));
        }

        return emitted;
    }

    private static ClassDefinition EmitException(
        IConstructContainer container, ErrorResponseModel error, string modelsNamespace) {
        // Allocated by NameAllocator against the same scope the schemas take their names from,
        // because a Smithy error shape wants the name its own payload record already holds. Set for
        // exactly the errors ShippedResponses.For declined, which is what this list holds.
        var definition = container.AddClass(error.ExceptionTypeName!);

        definition.Modifiers |= ComponentModifier.Public | ComponentModifier.Partial;
        definition.AddBaseType(
            TypeDefinition.Get("Hardened.Requests.Abstract.Errors", "StatusCodeException"));

        // No operation in the fallback any more, and there cannot be one: this type is shared by
        // every operation that declares the error. The description's own prose is still preferred,
        // and for a Smithy error that is the shape's @documentation.
        definition.Comment = DocComment.Format(error.Description)
            ?? $"The {error.StatusCode} response the description declares" +
               (error.Name == null ? "." : $" as '{error.Name}'.");

        var constructor = definition.AddConstructor(
            new CodeOutputComponent(
                error.Ref == null
                    ? $"base({error.StatusCode})"
                    : $"base({error.StatusCode}, value)") { Indented = false });

        constructor.Modifiers |= ComponentModifier.Public;

        if (error.Ref != null) {
            var payload = TypeDefinition.Get(
                modelsNamespace, NamingHelper.ToPascalCase(TypeMapper.GetRefName(error.Ref)));

            constructor.AddParameter(payload, "value");

            // Typed access to the body, which the base can only offer as object. Named Body
            // rather than hiding the base's Value with a new member: a reader seeing Value on the
            // derived type would have no way to tell it was not the one they knew about.
            var property = definition.AddProperty(payload, "Body");

            property.Modifiers |= ComponentModifier.Public;
            property.Set = null;
            property.Get.LambdaSyntax = true;

            // Raw code, so it bypasses the output context and would keep the short name while
            // every type around it is qualified - the one cast in this file that could still bind
            // to a consumer's type of the same name.
            var cast = new StringBuilder();
            payload.WriteTypeName(cast, TypeOutputMode.Global);

            property.Get.AddCode($"({cast})Value!;");
        }

        EmitHeaders(definition, constructor, error);

        return definition;
    }

    /// <summary>
    /// The headers the declared error carries, as constructor parameters and an
    /// <c>ApplyHeaders</c> override.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same division the union case type makes, and for the same reason: a description says a
    /// 429 carries a <c>Retry-After</c> and cannot say how long, so the type carries the
    /// declaration and whoever throws it carries the value.
    /// </para>
    /// <para>
    /// Without this the header was published in the document and never sent. An error's headers
    /// used to widen the operation into a response set purely so a case type existed to hang them
    /// on, which changed the signature of every operation that referenced a shared
    /// <c>components.responses</c> entry. The exception is where they belong: throws mode reaches
    /// an error by throwing, and <c>ExceptionToModelConverter</c> calls <c>ApplyHeaders</c> on any
    /// <c>IStatusCodeException</c> on its way out.
    /// </para>
    /// </remarks>
    private static void EmitHeaders(
        ClassDefinition definition, ConstructorDefinition constructor, ErrorResponseModel error) {
        if (error.Headers.Count == 0) {
            return;
        }

        foreach (var header in error.Headers) {
            // ParameterName is already the property's spelling, so the constructor's has to be
            // written down from it - taking it verbatim emits `RetryAfter = RetryAfter`, which
            // assigns the parameter to itself and leaves the property null.
            var parameter = constructor.AddParameter(
                TypeDefinition.Get(typeof(string)), NamingHelper.ToParameterName(header.ParameterName));

            var property = definition.AddProperty(
                TypeDefinition.Get(typeof(string)), header.ParameterName);

            property.Modifiers |= ComponentModifier.Public;
            property.Set = null;

            constructor.Assign(parameter).To("this." + property.Name);

            property.Comment = DocComment.Format(header.Description)
                ?? $"The value of the {header.Name} header this response declares.";
        }

        var method = definition.AddMethod("ApplyHeaders");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Override;
        method.SetReturnType(typeof(void));
        method.AddParameter(
            new GenericTypeDefinition(
                typeof(IDictionary<,>),
                new ITypeDefinition[] {
                    TypeDefinition.Get(typeof(string)),
                    TypeDefinition.Get("Microsoft.Extensions.Primitives", "StringValues")
                }),
            "headers");

        foreach (var header in error.Headers) {
            method.AddIndentedStatement(
                "headers[\"" + header.Name + "\"] = " + header.ParameterName);
        }
    }
}
