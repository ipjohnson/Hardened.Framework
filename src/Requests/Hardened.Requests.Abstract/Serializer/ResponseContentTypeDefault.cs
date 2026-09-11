namespace Hardened.Requests.Abstract.Serializer;

/// <summary>
/// What operations produce when they declare nothing themselves.
/// </summary>
/// <remarks>
/// <para>
/// The bottom rung of the cascade, and the one that is a registration rather than an attribute:
/// <c>services.AddSingleton(new ResponseContentTypeDefault("application/vnd.msgpack"))</c>. That is
/// how a serializer package makes itself the default for a whole service without an attribute on
/// every handler - it registers its <see cref="IResponseSerializer"/> and this together, and
/// importing the module is the whole of the configuration.
/// </para>
/// <para>
/// <b>Nearest wins, and nothing is combined.</b> An operation's <c>[Produces]</c> beats its class's,
/// which beats <c>[assembly: Produces]</c> on the handler's assembly, which beats this. Two
/// declarations do not compose into a third the way two authorization requirements do.
/// </para>
/// <para>
/// <b>The assembly beats this, and that is the decision the order turns on.</b> A library writing
/// <c>[assembly: Produces("text/csv")]</c> is saying something specific about its own handlers; this
/// is a blanket fallback for handlers that said nothing. Read the other way round, a host would
/// silently change what a library deliberately declared.
/// </para>
/// <para>
/// Registering more than one is an application saying two things; the last registration wins, which
/// is the rule serializers themselves follow.
/// </para>
/// </remarks>
public sealed class ResponseContentTypeDefault {
    public ResponseContentTypeDefault(params string[] contentTypes) {
        ContentTypes = contentTypes;
    }

    /// <summary>The media types, in the order the server prefers them.</summary>
    public IReadOnlyList<string> ContentTypes { get; }
}
