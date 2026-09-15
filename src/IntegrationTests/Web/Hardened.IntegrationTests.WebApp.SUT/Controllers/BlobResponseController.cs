using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;
using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.WebApp.SUT.Controllers;

/// <summary>
/// Bytes on the way out, bare and as the success case of a declared response set.
/// </summary>
/// <remarks>
/// <para>
/// <c>RawBodyController</c> is the inbound half. This is the other one, and the pair that matters is
/// <c>Bare</c> against <c>Blob</c>: the same bytes, the same declaration, one returned on their own
/// and one returned beside a 404.
/// </para>
/// <para>
/// The wrapped form answered <c>"f0VMRg=="</c> under <c>application/json</c> and published a 200
/// with no content, because everything the build asks about a response - is it bytes, can it commit
/// a content type, does it need a declaration - was asked of the return type, and the return type of
/// a response set is a model. Declaring the media type did not rescue it either: the success came
/// out right and the declared 404 answered 500 with an empty body, since nothing writes a
/// <c>NotFound</c> as an octet stream.
/// </para>
/// </remarks>
[BasePath("/blob-response")]
public class BlobResponseController
{
    /// <summary>An ELF header, because it is recognisable and is not valid UTF-8.</summary>
    private static readonly byte[] Elf = [0x7F, 0x45, 0x4C, 0x46];

    [Get("/bare/{id:int}")]
    [Produces("application/octet-stream")]
    public byte[] Bare(int id) => Elf;

    [Get("/blob/{id:int}")]
    [Produces("application/octet-stream")]
    public Response<byte[], NotFound> Blob(int id) => id > 0 ? Elf : new NotFound("blob");

    /// <summary>A stream, which the handler hands over rather than reading.</summary>
    [Get("/stream/{id:int}")]
    [Produces("application/octet-stream")]
    public Response<Stream, NotFound> StreamBlob(int id) =>
        id > 0 ? new MemoryStream(Elf) : new NotFound("blob");
}
