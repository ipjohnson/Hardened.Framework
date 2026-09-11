using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Runtime.Validation;
using Hardened.Requests.Serializers.MessagePack.Impl.Formatters;
using MessagePack;
using MessagePack.Formatters;

namespace Hardened.Requests.Serializers.MessagePack;

/// <summary>
/// Answers for the framework's error envelopes: <c>ErrorModel</c> and
/// <c>RequestValidationError</c>.
/// </summary>
/// <remarks>
/// <para>
/// Installed by this package, so a service needs nothing for its refusals to go out as MessagePack.
/// It is public for the other end: a .NET client reading a MessagePack error body composes options
/// with this rather than writing the two formatters again.
/// </para>
/// <code>
/// var options = MessagePackSerializerOptions.Standard
///     .WithResolver(CompositeResolver.Create(
///         [], [ErrorEnvelopeResolver.Instance, StandardResolver.Instance]));
/// </code>
/// <para>
/// The envelopes cannot carry <c>[MessagePackObject]</c> themselves - they live in
/// <c>Hardened.Requests.Abstract</c> and <c>Hardened.Requests.Runtime</c>, which every application
/// references and neither of which is taking a MessagePack dependency for two classes. The
/// formatters are written by hand beside this for that reason.
/// </para>
/// </remarks>
public sealed class ErrorEnvelopeResolver : IFormatterResolver {

    public static readonly ErrorEnvelopeResolver Instance = new();

    private ErrorEnvelopeResolver() { }

    public IMessagePackFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;

    private static class Cache<T> {
        public static readonly IMessagePackFormatter<T>? Formatter =
            (IMessagePackFormatter<T>?)Lookup(typeof(T));
    }

    private static object? Lookup(Type type) {
        if (type == typeof(ErrorModel)) {
            return ErrorModelFormatter.Instance;
        }

        if (type == typeof(RequestValidationError)) {
            return RequestValidationErrorFormatter.Instance;
        }

        return type == typeof(RequestValidationFieldError)
            ? RequestValidationFieldErrorFormatter.Instance
            : null;
    }
}
