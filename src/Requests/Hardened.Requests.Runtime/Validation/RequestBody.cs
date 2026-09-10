using ValidationModules;

namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// What a generated binder does with a body the handler declared it cannot do without.
/// </summary>
/// <remarks>
/// <para>
/// The binder used to end in a null-forgiving <c>!</c> and nothing else -
/// <c>parameters.body = (await …DeserializeRequestBody&lt;Quote&gt;(context))!;</c> - which is a
/// promise to the compiler rather than a check. <c>null</c> is a valid JSON document, so a caller
/// sending the four bytes <c>null</c> deserialized to null, the null reached the handler, and the
/// first dereference was a <c>NullReferenceException</c>: a 500 for a request the contract already
/// says is invalid. Every other malformed payload - <c>5</c>, <c>"text"</c>, <c>[]</c>, a truncated
/// object, no body at all - was a 400. <c>null</c> was the only one that was not.
/// </para>
/// <para>
/// Reported as <c>required</c> against the body rather than as a server fault, because that is what
/// a caller who sent no value has done, and it is the answer the same request gets when the body is
/// empty. <see cref="ValidationException"/> so the envelope is the one every other refusal
/// produces - the caller cannot tell which layer refused them, which is the point.
/// </para>
/// </remarks>
public static class RequestBody {

    /// <summary>
    /// <paramref name="value"/>, or a 400 naming <paramref name="field"/>.
    /// </summary>
    /// <param name="field">
    /// The handler's own body parameter identifier, which is what the generated validators and the
    /// deserializer's own errors are pathed under. Not the literal word "body": a handler that calls
    /// its parameter <c>request</c> reports <c>request</c> everywhere else.
    /// </param>
    /// <remarks>
    /// Emitted only for a parameter the handler declared non-nullable. A handler taking
    /// <c>Quote?</c> has said a null body is a case it handles, and gets the null.
    /// </remarks>
    public static T Required<T>(T? value, string field) {
        if (value is null) {
            throw new ValidationException(ValidationResult.FromErrors(
                new[] { new ValidationError(field, ValidationCodes.Required, $"{field} is required.") }));
        }

        return value;
    }
}
