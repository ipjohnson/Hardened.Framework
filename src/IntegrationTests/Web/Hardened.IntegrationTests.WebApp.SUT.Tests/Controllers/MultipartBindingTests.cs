using System.Net.Http.Headers;
using System.Text;
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Kestrel.Runtime;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Binding from a <c>multipart/form-data</c> body, through the real pipeline.
/// </summary>
public class MultipartBindingTests
{
    [HardenedTest]
    public async Task RequestBenchsUploadAnswersAsCarterDid(ITestWebApp testWebApp)
    {
        var response = await Uploads.Post(testWebApp, "/form/upload", Uploads.RequestBench());

        response.Assert.Ok();
        Assert.Equal(Uploads.Answer, await response.ReadTextAsync());
    }

    /// <summary>
    /// .NET's own client quotes its boundary, leaves names bare and sends <c>filename*</c> beside
    /// <c>filename</c>, which is how Carter's tests build the request.
    /// </summary>
    [HardenedTest]
    public async Task DotNetsMultipartContentIsRead(ITestWebApp testWebApp)
    {
        using var content = new MultipartFormDataContent();

        content.Add(new StringContent("qwertyuiopas"), "tenant");
        content.Add(new StringContent("0123456789abcdef"), "requestId");

        var file = new ByteArrayContent(Uploads.File);

        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        content.Add(file, "file", "forms.file.txt");

        var response = await testWebApp.Post(
            await content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken),
            "/form/upload",
            request => request.Headers["Content-Type"] = content.Headers.ContentType!.ToString()
        );

        response.Assert.Ok();
        Assert.Equal(Uploads.Answer, await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task CurlsMultipartBodyIsRead(ITestWebApp testWebApp)
    {
        const string boundary = "------------------------a1b2c3d4e5f60718";

        var response = await Uploads.Post(
            testWebApp,
            "/form/upload",
            Uploads.RequestBench(boundary: boundary),
            boundary
        );

        response.Assert.Ok();
        Assert.Equal(Uploads.Answer, await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task AModelBindsTheFieldsAndTheFile(ITestWebApp testWebApp)
    {
        var response = await Uploads.Post(testWebApp, "/form/upload-model", Uploads.RequestBench());

        response.Assert.Ok();
        Assert.Equal(Uploads.Answer, await response.ReadTextAsync());
    }

    /// <summary>A missing file is refused the way a missing field is.</summary>
    [HardenedTest]
    public async Task AMissingFileIsRequiredByName(ITestWebApp testWebApp)
    {
        var body = Encoding.UTF8.GetBytes(
            "--"
                + Uploads.Boundary
                + "\r\n"
                + "Content-Disposition: form-data; name=\"tenant\"\r\n\r\nqwertyuiopas\r\n"
                + "--"
                + Uploads.Boundary
                + "\r\n"
                + "Content-Disposition: form-data; name=\"requestId\"\r\n\r\n0123456789abcdef\r\n"
                + "--"
                + Uploads.Boundary
                + "--\r\n"
        );

        var response = await Uploads.Post(testWebApp, "/form/upload", body);

        response.Assert.BadRequest();

        var error = Assert.Single(response.Deserialize<RequestValidationError>()!.Errors!);

        Assert.Equal("file", error.Field);
        Assert.Equal("required", error.Code);
    }

    /// <summary>
    /// A body cut off inside a part is refused the way a malformed JSON body is.
    /// </summary>
    [HardenedTest]
    public async Task ATruncatedBodyIsAnInvalidBody(ITestWebApp testWebApp)
    {
        var whole = Uploads.RequestBench();

        var response = await Uploads.Post(testWebApp, "/form/upload", whole[..(whole.Length / 2)]);

        response.Assert.BadRequest();

        var error = Assert.Single(response.Deserialize<RequestValidationError>()!.Errors!);

        Assert.Equal("body", error.Field);
        Assert.Equal("invalid", error.Code);
    }

    [HardenedTest]
    public async Task ABodyPastTheCapIsTooLarge(ITestWebApp testWebApp)
    {
        var response = await Uploads.Post(
            testWebApp,
            "/form/upload",
            Uploads.RequestBench(new byte[150_000])
        );

        Assert.Equal(413, response.StatusCode);
    }

    /// <summary>Every byte of a file arrives, including the ones that are not UTF-8.</summary>
    [HardenedTest]
    public async Task AFileKeepsEveryByte(ITestWebApp testWebApp)
    {
        var binary = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        var response = await Uploads.Post(
            testWebApp,
            "/form/upload-echo",
            Uploads.RequestBench(binary)
        );

        response.Assert.Ok();
        Assert.Equal(binary, Uploads.Bytes(response));
    }

    [HardenedTest]
    public async Task SeveralFilesBindUnderOneName(ITestWebApp testWebApp)
    {
        var body = Encoding.UTF8.GetBytes(
            "--"
                + Uploads.Boundary
                + "\r\n"
                + "Content-Disposition: form-data; name=\"photos\"; filename=\"one.png\"\r\n\r\n1\r\n"
                + "--"
                + Uploads.Boundary
                + "\r\n"
                + "Content-Disposition: form-data; name=\"photos\"; filename=\"two.png\"\r\n\r\n22\r\n"
                + "--"
                + Uploads.Boundary
                + "--\r\n"
        );

        var response = await Uploads.Post(testWebApp, "/form/upload-many", body);

        response.Assert.Ok();
        Assert.Equal("one.png:1,two.png:2|none", response.Deserialize<string>());
    }
}

/// <summary>
/// The same uploads on a real socket, where the body is Kestrel's and cannot seek.
/// </summary>
[KestrelRuntime]
public class MultipartOverSocketTests
{
    [HardenedTest]
    public async Task RequestBenchsUploadAnswersAsCarterDid(ITestWebApp testWebApp)
    {
        var response = await Uploads.Post(testWebApp, "/form/upload", Uploads.RequestBench());

        response.Assert.Ok();
        Assert.Equal(Uploads.Answer, await response.ReadTextAsync());
    }

    [HardenedTest]
    public async Task AFileKeepsEveryByte(ITestWebApp testWebApp)
    {
        var binary = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray();

        var response = await Uploads.Post(
            testWebApp,
            "/form/upload-echo",
            Uploads.RequestBench(binary)
        );

        response.Assert.Ok();
        Assert.Equal(binary, Uploads.Bytes(response));
    }

    [HardenedTest]
    public async Task ABodyPastTheCapIsTooLarge(ITestWebApp testWebApp)
    {
        var response = await Uploads.Post(
            testWebApp,
            "/form/upload",
            Uploads.RequestBench(new byte[150_000])
        );

        Assert.Equal(413, response.StatusCode);
    }
}

/// <summary>RequestBench's <c>forms.multipart</c> request and the answer Carter recorded.</summary>
internal static class Uploads
{
    public const string Boundary = "rb-7c4f1e0a9d";

    public const string Answer = """
        {"file":{"name":"forms.file.txt","bytes":32762},"echo":{"tenant":"qwertyuiopas","requestId":"0123456789abcdef"}}
        """;

    /// <summary>A CSV file of RequestBench's length, with LF line ends like its payload.</summary>
    public static readonly byte[] File = CsvOf(32_762);

    public static byte[] RequestBench(byte[]? file = null, string boundary = Boundary)
    {
        using var body = new MemoryStream();

        void Write(string text) => body.Write(Encoding.UTF8.GetBytes(text));

        Write("--" + boundary + "\r\n");
        Write("Content-Disposition: form-data; name=\"tenant\"\r\n\r\nqwertyuiopas\r\n");
        Write("--" + boundary + "\r\n");
        Write("Content-Disposition: form-data; name=\"requestId\"\r\n\r\n0123456789abcdef\r\n");
        Write("--" + boundary + "\r\n");
        Write("Content-Disposition: form-data; name=\"file\"; filename=\"forms.file.txt\"\r\n");
        Write("Content-Type: text/plain\r\n\r\n");
        body.Write(file ?? File);
        Write("\r\n--" + boundary + "--\r\n");

        return body.ToArray();
    }

    public static Task<TestWebResponse> Post(
        ITestWebApp testWebApp,
        string path,
        byte[] body,
        string boundary = Boundary
    ) =>
        testWebApp.Post(
            body,
            path,
            request => request.Headers["Content-Type"] = "multipart/form-data; boundary=" + boundary
        );

    public static byte[] Bytes(TestWebResponse response)
    {
        using var copy = new MemoryStream();

        response.Body.Position = 0;
        response.Body.CopyTo(copy);

        return copy.ToArray();
    }

    private static byte[] CsvOf(int length)
    {
        var csv = new StringBuilder("id,name,category,priceCents,inStock\n");

        for (var row = 1; csv.Length < length; row++)
        {
            csv.Append(row).Append(",slate-lamp-").Append(row % 9973).Append(",tools,18928,true\n");
        }

        return Encoding.ASCII.GetBytes(csv.ToString(0, length));
    }
}
