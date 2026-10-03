namespace Hardened.Requests.Runtime.Validation;

/// <summary>
/// Thrown by the request deserializer when a body held values its enums do not declare, carrying
/// what it read past them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Body"/> holds each undeclared value as its type's default. The binder puts it on the
/// parameters, so the constraints on the body's other members are checked and reported beside
/// these errors. A constraint error under a field named here is dropped, as it is for any field
/// that failed to bind. See <see cref="ParameterBindingException"/>.
/// </para>
/// <para>
/// <see cref="Body"/> is null where a later read failed for another reason, such as a member of the
/// wrong type. That failure is among the errors, and nothing was read to check constraints on, so
/// every constraint error under <see cref="Field"/> is dropped.
/// </para>
/// </remarks>
public sealed class BodyBindingException : ValidationException
{
    public BodyBindingException(
        ValidationModules.ValidationResult validationResult,
        string field,
        object? body,
        Exception inner
    )
        : base(validationResult, inner)
    {
        Field = field;
        Body = body;
    }

    /// <summary>The body parameter's name, which every error here is under.</summary>
    public string Field { get; }

    /// <summary>The body as read, or null where it could not be read to the end.</summary>
    public object? Body { get; }
}
