using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// A body parameter declared as <c>byte[]</c> or <c>Stream</c>, which is the payload rather than a
/// shape to read out of one.
/// </summary>
/// <remarks>
/// <para>
/// Every handler here answers a fact about the bytes it received rather than echoing them, so a
/// test asserts that the handler saw the payload rather than that the pipeline round-tripped
/// something. The interesting failures are all "the handler never ran": the binder used to hand
/// every body to the JSON reader whatever its type, so a blob was refused
/// <c>'0x7F' is an invalid start of a value</c> before the handler was reached, under every
/// <c>Content-Type</c> including none.
/// </para>
/// <para>
/// There was no end-to-end test of a blob request body anywhere in this repository. The Smithy
/// fixture declares one and nothing sends a body to it, which is how this survived a release.
/// </para>
/// </remarks>
[BasePath("/raw-body")]
public class RawBodyController
{
    public record Received(int Length, int First, int Last);

    private static Received Describe(byte[] bytes) =>
        new(bytes.Length, bytes.Length == 0 ? -1 : bytes[0], bytes.Length == 0 ? -1 : bytes[^1]);

    [Put("/bytes")]
    public Received Bytes(byte[] payload) => Describe(payload);

    /// <summary>A handler that does its own reading, which is what asking for a stream means.</summary>
    [Put("/stream")]
    public async Task<Received> Streamed(Stream payload)
    {
        var buffer = new MemoryStream();

        await payload.CopyToAsync(buffer);

        return Describe(buffer.ToArray());
    }

    /// <summary>Nullable, so a request with no body reaches the handler rather than being refused.</summary>
    [Put("/optional")]
    public Received OptionalBytes(byte[]? payload) => Describe(payload ?? []);
}
