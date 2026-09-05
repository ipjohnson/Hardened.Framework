namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// The status a response type answers with, available from the type rather than from an instance.
/// </summary>
/// <remarks>
/// <para>
/// Three readers ask a response type for its status, and each can only reach one of the three
/// answers. The generator reads <see cref="HttpStatusAttribute"/> at compile time. The pipeline
/// reads <see cref="IHttpStatusResponse.Status"/> off an instance. A test has neither: the client
/// throws its own model or hands back an envelope, so there is no instance of this type to read and
/// no compilation of the handler to inspect.
/// </para>
/// <para>
/// This is that third answer, and it also makes the other two agree by construction - each record
/// now reads <c>Status =&gt; StatusCode</c>, so there is one literal per type where there were two.
/// </para>
/// <para>
/// Distinct from <see cref="IStatusCode"/>, which names a status that has no record of its own:
/// there the type <i>is</i> the status, here the type is a response that has one. They cannot be
/// the same interface, because a response already carries an instance <c>Status</c> and a type
/// cannot declare a static and an instance member under one name.
/// </para>
/// <para>
/// A separate interface rather than a member on <see cref="IHttpStatusResponse"/>, because a static
/// abstract added there would break every implementation already compiled against it, an
/// application's own response types included. Opting in costs one line and nothing breaks by
/// standing still.
/// </para>
/// </remarks>
public interface IDeclaresStatus {

    /// <summary>The status, the same one <see cref="HttpStatusAttribute"/> declares.</summary>
    static abstract int StatusCode { get; }
}
