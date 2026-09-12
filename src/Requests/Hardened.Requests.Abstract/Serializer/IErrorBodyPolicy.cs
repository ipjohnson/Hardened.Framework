namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// One answer, for the whole service, to what a failed request's body is written as.
/// </summary>
/// <remarks>
/// Registered by the generated routing table from <c>[ErrorBodies]</c> on the entry point or
/// <c>x-hardened-error-bodies</c> at a description's root, and defaulted to
/// <see cref="ErrorBodyFormat.Negotiated"/> where neither says otherwise - so a service that says
/// nothing carries no registration for this and behaves exactly as it did.
/// </remarks>
public interface IErrorBodyPolicy {

    ErrorBodyFormat Format { get; }
}

/// <inheritdoc />
public sealed class ErrorBodyPolicy : IErrorBodyPolicy {

    public ErrorBodyPolicy(ErrorBodyFormat format = ErrorBodyFormat.Negotiated) {
        Format = format;
    }

    public ErrorBodyFormat Format { get; }
}
