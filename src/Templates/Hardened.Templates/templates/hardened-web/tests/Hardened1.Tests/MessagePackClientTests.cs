using System.Net.Http.Headers;
using Hardened.Requests.Abstract.Headers;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Hardened1.Client;

namespace Hardened1.Tests;

/// <summary>
/// The generated client's contracts and serializer against the real service, over MessagePack.
/// </summary>
/// <remarks>
/// <para>
/// The Liquid templates put [MessagePackObject] and [Key] on every generated contract, and that
/// alone changes nothing on the wire - Refit reads its format from RefitSettings.ContentSerializer
/// and defaults to System.Text.Json. This is what proves the pair actually agree: the client's own
/// serializer, the client's own generated types, and the service, over one round trip in each
/// direction.
/// </para>
/// <para>
/// <b>Through ITestWebApp rather than the Refit interface, and that is a limitation worth knowing
/// about.</b> Refitter pins the document's media types on each operation as
/// [Headers("Accept: ...")], in document order, and a header on the request wins over
/// HttpClient.DefaultRequestHeaders - so switching the whole interface to MessagePack means
/// overriding Accept per request. Refit's own error path then gets in the way: ApiException carries
/// the response content as a <c>string</c>, so a binary error body is decoded and re-encoded before
/// any serializer sees it and arrives corrupt. Success bodies are unaffected, which is what this
/// covers. See the README.
/// </para>
/// </remarks>
public class MessagePackClientTests {

    private static readonly MessagePackContentSerializer Serializer = new();

    [HardenedTest]
    public async Task TheClientsContractsRoundTripAgainstTheService(ITestWebApp app) {
        // Written by the client's own serializer, which is what makes this a round trip rather
        // than a test of bytes this file made up.
        using var written = Serializer.ToHttpContent(
            new ClientModels.NewTodo { Title = "Read the generated client" });

        var body = await written.ReadAsByteArrayAsync();

        var created = await app.Request(
            "POST", null, "/todos",
            request => {
                request.RawBody(body, MessagePackContentSerializer.ContentType);
                request.Headers[KnownHeaders.Accept] = MessagePackContentSerializer.ContentType;
            });

        Assert.Equal(201, created.StatusCode);

        Assert.StartsWith(
            MessagePackContentSerializer.ContentType,
            created.Headers[KnownHeaders.ContentType].ToString());

        var todo = await Read<ClientModels.Todo>(created);

        Assert.NotNull(todo);
        Assert.Equal("Read the generated client", todo.Title);
        Assert.True(todo.Id > 0);
    }

    /// <summary>
    /// And the declared 404, whose body the client reads as the type the document named for it.
    /// </summary>
#if (codeFirst)
    /// <remarks>
    /// That type is Hardened's own NotFound. It cannot carry [MessagePackObject] - it lives in
    /// Hardened.Web.Runtime, which is not taking a MessagePack dependency - so the serializer
    /// package writes it by hand, and the client reads it into the contract Refitter generated from
    /// the published document. This is the two halves of that agreeing.
    /// </remarks>
#endif
    [HardenedTest]
    public async Task ADeclaredRefusalRoundTripsToo(ITestWebApp app) {
        var response = await app.Get(
            "/todos/9999",
            request => request.Headers[KnownHeaders.Accept] = MessagePackContentSerializer.ContentType);

        response.Assert.NotFound();

        Assert.StartsWith(
            MessagePackContentSerializer.ContentType,
            response.Headers[KnownHeaders.ContentType].ToString());

#if (codeFirst)
        var refusal = await Read<ClientModels.NotFound>(response);
#endif
#if (specFirst)
        var refusal = await Read<ClientModels.Problem>(response);
#endif

        Assert.NotNull(refusal);
        Assert.Equal(404, refusal.Status);
    }

    /// <summary>Through the client's serializer, which is the point of the assertion.</summary>
    private static async Task<T?> Read<T>(TestWebResponse response) {
        response.Body.Position = 0;

        var content = new StreamContent(response.Body);

        content.Headers.ContentType =
            new MediaTypeHeaderValue(MessagePackContentSerializer.ContentType);

        return await Serializer.FromHttpContentAsync<T>(content);
    }
}
