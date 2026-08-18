using System.Text;
using DependencyModules.Runtime.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Forms;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Serializer;

namespace Hardened.Requests.Runtime.Forms;

/// <summary>
/// Reads an <c>application/x-www-form-urlencoded</c> body into its fields.
/// </summary>
/// <remarks>
/// <para>
/// On <c>IKnownServices</c> rather than as a member of <c>IExecutionRequest</c>, and that is the
/// whole reason the binding costs what it does. A <c>Form</c> property on the request would be a
/// change to the contract every transport implements and the conformance suite covers - three
/// adapters here and three in <c>Hardened.Amz</c>. <c>IKnownServices</c> has one implementation,
/// resolved from the container, so every host gets this without knowing it happened.
/// </para>
/// <para>
/// Stateless, because that implementation is a singleton. The generated binder reads the form once
/// per handler and holds it in a local, so nothing needs a per-request cache to avoid parsing
/// twice.
/// </para>
/// </remarks>
[SingletonService(Using = RegistrationType.Try)]
public class FormReader : IFormReader {

    public async ValueTask<IFormCollection> ReadForm(IExecutionContext context) {
        if (!MediaType.Matches(context.Request.ContentType, KnownContentType.FormUrlEncoded)) {
            return EmptyFormCollection.Instance;
        }

        var body = context.Request.Body;

        if (body == null!) {
            return EmptyFormCollection.Instance;
        }

        // From the start, because a filter ahead of this one may have read it. RetryFilter already
        // rewinds a seekable body between attempts for the same reason.
        if (body.CanSeek) {
            body.Position = 0;
        }

        string content;

        // leaveOpen, because the body is the transport's and the response has not been written yet.
        // Disposing the reader would close a stream something downstream may still be holding.
        using (var reader = new StreamReader(body, Encoding.UTF8, true, 1024, leaveOpen: true)) {
            content = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        // Put it back for whatever reads next. A handler binding both form fields and a body model
        // is reported at build time, but a filter reading the body is not something the generator
        // can see.
        if (body.CanSeek) {
            body.Position = 0;
        }

        return UrlEncodedParser.Parse(content);
    }
}
