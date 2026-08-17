using Microsoft.Extensions.Primitives;

namespace Hardened.Requests.Abstract.Errors;

/// <summary>
/// An exception that names the status code and body the response should carry.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline could previously distinguish only two outcomes from a thrown exception: 400 for a
/// <c>BadRequestException</c> or a <c>FormatException</c>, and 500 for everything else. A
/// specification declaring a 404 with its own payload had no way to be honoured - the response a
/// document promised could not be produced at all, whatever the handler did.
/// </para>
/// <para>
/// Deriving from this says what to send. <see cref="Value"/> is the body when the specification
/// declared a shape for it; without one the pipeline falls back to its usual error model, so an
/// undocumented status still produces a sensible response rather than an empty one.
/// </para>
/// <para>
/// Override <see cref="ApplyHeaders"/> for a status that is not well-formed without one - a 401 and
/// its <c>WWW-Authenticate</c> challenge being the case that prompted it.
/// </para>
/// </remarks>
public class StatusCodeException : Exception, IStatusCodeException {

    public StatusCodeException(int statusCode, object? value = null, string? message = null)
        : base(message ?? "The request produced status " + statusCode + ".") {
        StatusCode = statusCode;
        Value = value;
    }

    public StatusCodeException(int statusCode, object? value, string message, Exception inner)
        : base(message, inner) {
        StatusCode = statusCode;
        Value = value;
    }

    /// <summary>The status the response carries.</summary>
    public int StatusCode { get; }

    /// <summary>The body, or null to use the pipeline's error model.</summary>
    public object? Value { get; }

    /// <summary>
    /// Adds nothing. A status that needs a header overrides this; most do not.
    /// </summary>
    public virtual void ApplyHeaders(IDictionary<string, StringValues> headers) { }
}
