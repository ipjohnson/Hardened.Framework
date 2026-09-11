using Hardened.Requests.Abstract.Authorization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Runtime.Validation;
using Hardened.Requests.Serializers.MessagePack.Impl.Formatters;
using Hardened.Web.Runtime.Responses;
using MessagePack;
using MessagePack.Formatters;

namespace Hardened.Requests.Serializers.MessagePack;

/// <summary>
/// Answers for the response bodies Hardened itself ships.
/// </summary>
/// <remarks>
/// <para>
/// The two error envelopes - <c>ErrorModel</c> and <c>RequestValidationError</c> - and every
/// built-in response type that reaches a serializer. Installed by this package, so an operation
/// declaring <c>application/x-msgpack</c> can answer a 404 or a refusal without the application
/// arranging anything.
/// </para>
/// <para>
/// <b>These types cannot carry <c>[MessagePackObject]</c>.</b> They live in
/// <c>Hardened.Requests.Abstract</c>, <c>.Runtime</c> and <c>Hardened.Web.Runtime</c>, which every
/// application references and none of which is taking a MessagePack dependency for them.
/// MessagePack's source generator writes a formatter only for an annotated type, so the formatters
/// are written by hand where the dependency already is.
/// </para>
/// <para>
/// <b>Only the ones that reach a serializer.</b> The generated dispatch assigns
/// <c>ICarriesResponseBody.Body</c> rather than the wrapper, so <c>Created&lt;T&gt;</c>,
/// <c>NotFound&lt;T&gt;</c> and the other generic wrappers send their payload and never arrive
/// here; a type with <c>HasBody =&gt; false</c> sets <c>ShouldSerialize</c> to false and writes
/// nothing at all. What is left is the problem-shaped records, and they are all here.
/// </para>
/// <para>
/// Public for the other end: a .NET client reading one of these bodies composes options with this
/// rather than writing the formatters again.
/// </para>
/// <code>
/// var options = MessagePackSerializerOptions.Standard
///     .WithResolver(CompositeResolver.Create(
///         [], [HardenedFormatterResolver.Instance, StandardResolver.Instance]));
/// </code>
/// </remarks>
public sealed class HardenedFormatterResolver : IFormatterResolver {

    public static readonly HardenedFormatterResolver Instance = new();

    private HardenedFormatterResolver() { }

    public IMessagePackFormatter<T>? GetFormatter<T>() => Cache<T>.Formatter;

    /// <summary>
    /// Resolved once per type by the runtime rather than looked up per call, which is what
    /// <c>IFormatterResolver</c> implementations are expected to do.
    /// </summary>
    private static class Cache<T> {
        public static readonly IMessagePackFormatter<T>? Formatter =
            (IMessagePackFormatter<T>?)Lookup(typeof(T));
    }

    /// <summary>
    /// One of the fifteen response types whose whole body is a <c>detail</c> and the three members
    /// the type itself decides.
    /// </summary>
    private static ProblemFormatter<T> Problem<T>(
        Func<T, string> type, Func<T, string> title, Func<T, string?> detail, Func<string?, T> create)
        where T : class, Hardened.Requests.Abstract.Responses.IHttpStatusResponse =>
        new(type, title, detail, create);

    private static object? Lookup(Type type) {
        // The envelopes.
        if (type == typeof(ErrorModel)) return ErrorModelFormatter.Instance;
        if (type == typeof(RequestValidationError)) return RequestValidationErrorFormatter.Instance;
        if (type == typeof(RequestValidationFieldError)) return RequestValidationFieldErrorFormatter.Instance;

        // The four response types that carry a member beyond detail, and the one thing one of them
        // carries.
        if (type == typeof(NotFound)) return NotFoundFormatter.Instance;
        if (type == typeof(RateLimited)) return RateLimitedFormatter.Instance;
        if (type == typeof(ServiceUnavailable)) return ServiceUnavailableFormatter.Instance;
        if (type == typeof(Unauthorized)) return UnauthorizedFormatter.Instance;
        if (type == typeof(AuthorizationChallenge)) return AuthorizationChallengeFormatter.Instance;

        // And the fifteen that do not. One line each, and the accessors rather than constants
        // because a type's own answer is the one that cannot drift from it.
        if (type == typeof(BadRequest)) return Problem<BadRequest>(v => v.Type, v => v.Title, v => v.Detail, d => new BadRequest(d));
        if (type == typeof(PaymentRequired)) return Problem<PaymentRequired>(v => v.Type, v => v.Title, v => v.Detail, d => new PaymentRequired(d));
        if (type == typeof(Forbidden)) return Problem<Forbidden>(v => v.Type, v => v.Title, v => v.Detail, d => new Forbidden(d));
        if (type == typeof(Conflict)) return Problem<Conflict>(v => v.Type, v => v.Title, v => v.Detail, d => new Conflict(d));
        if (type == typeof(Gone)) return Problem<Gone>(v => v.Type, v => v.Title, v => v.Detail, d => new Gone(d));
        if (type == typeof(PreconditionFailed)) return Problem<PreconditionFailed>(v => v.Type, v => v.Title, v => v.Detail, d => new PreconditionFailed(d));
        if (type == typeof(PreconditionRequired)) return Problem<PreconditionRequired>(v => v.Type, v => v.Title, v => v.Detail, d => new PreconditionRequired(d));
        if (type == typeof(ContentTooLarge)) return Problem<ContentTooLarge>(v => v.Type, v => v.Title, v => v.Detail, d => new ContentTooLarge(d));
        if (type == typeof(UnsupportedMediaType)) return Problem<UnsupportedMediaType>(v => v.Type, v => v.Title, v => v.Detail, d => new UnsupportedMediaType(d));
        if (type == typeof(UnprocessableContent)) return Problem<UnprocessableContent>(v => v.Type, v => v.Title, v => v.Detail, d => new UnprocessableContent(d));
        if (type == typeof(RequestTimeout)) return Problem<RequestTimeout>(v => v.Type, v => v.Title, v => v.Detail, d => new RequestTimeout(d));
        if (type == typeof(InternalServerError)) return Problem<InternalServerError>(v => v.Type, v => v.Title, v => v.Detail, d => new InternalServerError(d));
        if (type == typeof(NotImplemented)) return Problem<NotImplemented>(v => v.Type, v => v.Title, v => v.Detail, d => new NotImplemented(d));
        if (type == typeof(BadGateway)) return Problem<BadGateway>(v => v.Type, v => v.Title, v => v.Detail, d => new BadGateway(d));

        return type == typeof(GatewayTimeout)
            ? Problem<GatewayTimeout>(v => v.Type, v => v.Title, v => v.Detail, d => new GatewayTimeout(d))
            : null;
    }
}
