using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.Idl;
using Hardened.Generation;

namespace Hardened.Idl.Emitters;

/// <summary>
/// <c>AsException()</c> on the body a generated error carries, so the type is named once.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this removes.</b> A generated error is an exception wrapping the payload the description
/// declared, and in Smithy both are named after the same shape - so the throw reads
/// <c>new AccountNotFoundException(new AccountNotFound("no account"))</c>, which is a declaration of
/// the type followed by a repetition of it.
/// </para>
/// <para>
/// <b>The idiom is the framework's own.</b> <c>ResponseExceptionExtensions.AsException</c> exists for
/// exactly this noise on the shipped records - <c>new NotFound("todo").AsException()</c> rather than
/// naming <c>NotFound</c> twice - and its remarks say so. That one is generic over
/// <c>IHttpStatusResponse</c> and cannot reach a payload, which carries no status of its own, so the
/// generated overload is what extends the same verb to a declared error's body.
/// </para>
/// <para>
/// <b>Emitted only where the payload names one error.</b> Two errors over one schema would be two
/// overloads with identical signatures, which is CS0111 - <c>components/responses</c> lets an author
/// declare <c>PetMissing</c> and <c>PetLocked</c> both carrying <c>ApiError</c>, and there is no
/// single exception an <c>ApiError</c> means. A Smithy model meets the condition by construction,
/// because <c>@error</c> and <c>@httpError</c> live on the shape.
/// </para>
/// </remarks>
internal static class ErrorFactoryEmitter {

    /// <summary>The holder's name, which is the file's plus a suffix.</summary>
    /// <remarks>
    /// Reserved by <c>NameAllocator</c> beside <c>{file}Patterns</c> and
    /// <c>{file}JsonTypeInfoResolver</c>, so a schema of this name is renamed rather than colliding
    /// with it. An extension method has to live in a non-generic static class, so there is nowhere
    /// else to put these.
    /// </remarks>
    public static string HolderName(string specFileName) =>
        NamingHelper.ToPascalCase(specFileName) + "Errors";

    /// <param name="errors">
    /// The errors that got a generated exception, which is <see cref="ErrorResponseEmitter"/>'s own
    /// input. An error that binds to a shipped record already has <c>AsException()</c> through the
    /// generic extension, and one with no declared body is already thrown by naming its type once.
    /// </param>
    public static ClassDefinition? Emit(
        IConstructContainer container, IReadOnlyList<ErrorResponseModel> errors,
        string modelsNamespace, string specFileName) {
        var byPayload = new Dictionary<string, ErrorResponseModel?>(System.StringComparer.Ordinal);

        foreach (var error in errors) {
            if (error.Ref == null || error.ExceptionTypeName == null) {
                continue;
            }

            var payload = NamingHelper.ToPascalCase(TypeMapper.GetRefName(error.Ref));

            // Null marks a payload more than one error claims. Recorded rather than removed, so a
            // third error over the same schema cannot put it back.
            byPayload[payload] = byPayload.ContainsKey(payload) ? null : error;
        }

        ClassDefinition? holder = null;

        // Ordered by the payload type, so the emitted file is byte-stable between builds whatever
        // order the operations were walked in.
        var payloads = new List<string>(byPayload.Keys);

        payloads.Sort(System.StringComparer.Ordinal);

        foreach (var payload in payloads) {
            var error = byPayload[payload];

            if (error == null) {
                continue;
            }

            holder ??= CreateHolder(container, specFileName);

            EmitFactory(holder, error, payload, modelsNamespace);
        }

        return holder;
    }

    private static ClassDefinition CreateHolder(
        IConstructContainer container, string specFileName) {
        var holder = container.AddClass(HolderName(specFileName));

        holder.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        holder.Comment = DocComment.Format(
            "Throwing shorthand for the errors this description declares a type for. One method " +
            "per payload that names a single error, so the exception is inferred rather than " +
            "written out beside the body it carries.");

        return holder;
    }

    private static void EmitFactory(
        ClassDefinition holder, ErrorResponseModel error, string payload, string modelsNamespace) {
        var method = holder.AddMethod("AsException");

        method.Modifiers |= ComponentModifier.Public | ComponentModifier.Static;
        method.SetReturnType(TypeDefinition.Get(modelsNamespace, error.ExceptionTypeName!));

        var parameter = method.AddParameter(
            TypeDefinition.Get(modelsNamespace, payload), "body");

        parameter.This = true;

        method.LambdaSyntax = true;
        method.AddCode("new(body);");

        method.Comment = DocComment.Format(
            $"The declared {error.StatusCode} carrying this body, as the exception that produces " +
            "it.");
    }
}
