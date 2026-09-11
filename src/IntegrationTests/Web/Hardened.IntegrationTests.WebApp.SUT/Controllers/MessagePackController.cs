using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Serializers.MessagePack;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;
using MessagePack;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Operations that answer MessagePack, through
/// <c>Hardened.Requests.Serializers.MessagePack</c>.
/// </summary>
/// <remarks>
/// <para>
/// The fixture that makes the whole path real at once: the build diagnostic sees a serializer for
/// the media type, the document describes both representations, and the runtime picks between them
/// from <c>Accept</c>.
/// </para>
/// </remarks>
[BasePath("/msgpack")]
public class MessagePackController {

    /// <summary>
    /// A reading, annotated the way MessagePack requires.
    /// </summary>
    /// <remarks>
    /// <c>[MessagePackObject]</c> and <c>partial</c> are what MessagePack's source generator needs
    /// to write a formatter for this type. There is no reflection fallback in the resolver chain
    /// this package installs, so a model without them fails the same way here as after a NativeAOT
    /// publish rather than only after one.
    /// </remarks>
    [MessagePackObject]
    public partial record Reading([property: Key(0)] string Sensor, [property: Key(1)] int Value);

    /// <summary>
    /// Two representations of one response. The client's <c>Accept</c> decides, and a client
    /// expressing no preference gets the first.
    /// </summary>
    [Get("/negotiated/{id}")]
    [Produces(KnownContentType.Json, MessagePackContentType.Value)]
    public Reading Negotiated(int id) => new("sensor-" + id, id * 3);

    /// <summary>
    /// MessagePack alone, which is the shape that binds at composition rather than negotiating:
    /// one declared media type under the default negotiation mode resolves its serializer once.
    /// </summary>
    [Get("/packed/{id}")]
    [Produces(MessagePackContentType.Value)]
    public Reading Packed(int id) => new("sensor-" + id, id * 3);

    /// <summary>
    /// A MessagePack request body. Nothing declares it: a deserializer is chosen by the inbound
    /// <c>Content-Type</c>, so this reads MessagePack from a client that sends it and JSON from one
    /// that does not.
    /// </summary>
    [Post("/round")]
    [Produces(KnownContentType.Json, MessagePackContentType.Value)]
    public Reading Round(Reading reading) => reading with { Value = reading.Value + 1 };

    /// <summary>
    /// Two declared statuses, both answerable as MessagePack.
    /// </summary>
    /// <remarks>
    /// The 404 body is <c>NotFound</c>, a framework type that cannot carry
    /// <c>[MessagePackObject]</c> - it lives in <c>Hardened.Web.Runtime</c>, which is not taking a
    /// MessagePack dependency. Before <c>HardenedFormatterResolver</c> the writer was asked for one
    /// and found no formatter, so the media type could only be declared on an operation with a
    /// single outcome. This is the fixture for the other case.
    /// </remarks>
    [Get("/declared/{id}")]
    [Produces(KnownContentType.Json, MessagePackContentType.Value)]
    public Response<Reading, NotFound> Declared(int id) {
        if (id > 100) {
            return new NotFound("reading", $"No reading has id {id}.");
        }

        return new Reading("sensor-" + id, id * 3);
    }
}
