using System.Net.Http.Headers;
using Hardened.Web.Testing;
using Hardened1.Client;
using Refit;

namespace Hardened1.Tests;

/// <summary>
/// Builds the generated client so that it speaks MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// [assembly: RefitTesting] builds a Refit interface over the pipeline with default RefitSettings,
/// which is System.Text.Json - so without this the client would carry MessagePack attributes and
/// send JSON. A factory for the interface wins over that route, which is the seam
/// ITestClientFactory exists for.
/// </para>
/// <para>
/// Every test in TodoTests therefore runs over MessagePack, the typed 404, 409 and 400 bodies
/// included. Those error bodies arrive as JSON, because the application asks for that with
/// [ErrorBodies(ErrorBodyFormat.Json)] - see TemplateModuleNameLibrary. The content serializer reads
/// either.
/// </para>
/// </remarks>
public class MessagePackClientFactory : ITestClientFactory<ITemplateModuleNameClient> {

    private static readonly RefitSettings Settings =
        new() { ContentSerializer = new MessagePackContentSerializer() };

    /// <summary>
    /// What a consumer of Hardened1.Client writes against their own HttpClient, minus the header -
    /// which is theirs to set, the same way this one does below.
    /// </summary>
    public ITemplateModuleNameClient Create(HttpClient http) =>
        RestService.For<ITemplateModuleNameClient>(http, Settings);

    /// <summary>
    /// What the harness uses, with a handler in front of the pipeline that states the preference.
    /// </summary>
    public ITemplateModuleNameClient Create(TestClientContext context) {
        ArgumentNullException.ThrowIfNull(context);

        return RestService.For<ITemplateModuleNameClient>(
            context.CreateHttpClient(new PrefersMessagePack()), Settings);
    }

    /// <summary>
    /// Replaces the two media types Refitter wrote into the interface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Refitter takes them from the document, in the order the document lists them, and pins them
    /// on each operation - [Headers("Accept: application/json, application/x-msgpack",
    /// "Content-Type: application/json")]. A header on the request wins over
    /// HttpClient.DefaultRequestHeaders, so setting a default would not reach either.
    /// </para>
    /// <para>
    /// Both halves matter and they fail differently. Accept left alone means the service answers
    /// JSON and the client's MessagePack deserializer cannot read it. Content-Type left alone means
    /// the service reads a MessagePack body as JSON and answers 400 - "'0x91' is an invalid start
    /// of a value", 0x91 being the header of a one-element array, which is what a keyed object is.
    /// </para>
    /// <para>
    /// The contract leads with JSON on purpose - a browser gets something it can render - so a
    /// client preferring the other representation says so per request, which is what any consumer
    /// of this package does.
    /// </para>
    /// </remarks>
    private sealed class PrefersMessagePack : DelegatingHandler {

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) {
            ArgumentNullException.ThrowIfNull(request);

            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(
                new MediaTypeWithQualityHeaderValue(MessagePackContentSerializer.ContentType));

            if (request.Content != null) {
                request.Content.Headers.ContentType =
                    new MediaTypeHeaderValue(MessagePackContentSerializer.ContentType);
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
