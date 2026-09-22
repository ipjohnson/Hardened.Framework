using System.Text;
using Hardened.Shared.Testing.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.LambdaHttp.SUT.Tests;

/// <summary>
/// A binary upload through the Lambda web host, which is where it has to survive API Gateway's
/// encoding of the event.
/// </summary>
/// <remarks>
/// The host read every request body as UTF-8 and sent it as text, so a file part with bytes that
/// are not UTF-8 arrived changed. An HTTP API delivers a body that is not a text media type as
/// base64, and the host now does the same.
/// </remarks>
public class UploadTests
{
    [HardenedTest]
    public async Task AFileKeepsEveryByte(ITestWebApp app)
    {
        var binary = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        using var body = new MemoryStream();

        body.Write(
            Encoding.ASCII.GetBytes(
                "--b\r\nContent-Disposition: form-data; name=\"file\"; filename=\"bytes.bin\"\r\n"
                    + "Content-Type: application/octet-stream\r\n\r\n"
            )
        );
        body.Write(binary);
        body.Write(Encoding.ASCII.GetBytes("\r\n--b--\r\n"));

        var response = await app.Post(
            body.ToArray(),
            "/uploads",
            request => request.Headers["Content-Type"] = "multipart/form-data; boundary=b"
        );

        response.Assert.Ok();
        Assert.Equal(Convert.ToHexString(binary), response.Deserialize<string>());
    }
}
