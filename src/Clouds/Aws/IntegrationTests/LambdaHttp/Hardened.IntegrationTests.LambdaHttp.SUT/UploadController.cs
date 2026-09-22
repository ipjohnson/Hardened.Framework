using Hardened.Requests.Abstract.Forms;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.LambdaHttp.SUT;

/// <summary>
/// A multipart upload, so a binary request body has to cross API Gateway's event encoding intact.
/// </summary>
public class UploadController
{
    /// <summary>The file's bytes as hex, so the answer is text whatever the file held.</summary>
    [Post("/uploads")]
    public string Hex([FromForm] IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var copy = new MemoryStream();

        stream.CopyTo(copy);

        return Convert.ToHexString(copy.ToArray());
    }
}
