using System.IO.Compression;
using System.Text;
using Hardened.IntegrationTests.WebApp.SUT.Controllers;
using Hardened.Requests.Abstract.Headers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests.Controllers;

/// <summary>
/// Compression in both directions, through the real pipeline: the application-wide default that
/// <c>[Enable&lt;ResponseCompression&gt;]</c> installs on this fixture, the per-operation
/// declarations on <see cref="CompressionController"/>, and the request-side filter every
/// application gets.
///
/// <para>
/// The test client sends <c>Accept-Encoding: gzip</c> unless a test sets the header, and its
/// decoded accessors undo whatever coding came back - so a test elsewhere in this fixture never
/// notices that its responses are now compressed, and the tests here read the raw body when the
/// coding is the point.
/// </para>
/// </summary>
public class CompressionTests {

    private static bool LooksGzip(TestWebResponse response) {
        response.Body.Position = 0;

        var looksGzip = response.Body.Length > 2 && response.Body.ReadByte() == 0x1f && response.Body.ReadByte() == 0x8b;

        // Rewound, because the decoded accessors read from wherever the body was left.
        response.Body.Position = 0;

        return looksGzip;
    }

    private static string Coding(TestWebResponse response) =>
        response.Headers.TryGetValue(KnownHeaders.ContentEncoding, out var value) ? value.ToString() : "";

    private static byte[] GZipped(string content) {
        var output = new MemoryStream();

        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, true)) {
            var bytes = Encoding.UTF8.GetBytes(content);

            gzip.Write(bytes, 0, bytes.Length);
        }

        return output.ToArray();
    }

    // ---------------------------------------------------------------- responses

    [HardenedTest]
    public async Task AJsonResponseIsGzippedForAClientThatAcceptsIt(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/compression/readings");

        response.Assert.Ok();

        Assert.Equal("gzip", Coding(response));
        Assert.Contains("Accept-Encoding", response.Headers[KnownHeaders.Vary].ToString());
        Assert.True(LooksGzip(response));
        Assert.Equal(20, response.Deserialize<List<CompressionController.Reading>>().Count);
    }

    [HardenedTest]
    public async Task AClientAcceptingNothingIsServedPlain(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/compression/readings",
            request => request.Headers[KnownHeaders.AcceptEncoding] = "identity");

        response.Assert.Ok();

        Assert.Equal("", Coding(response));
        Assert.False(LooksGzip(response));
        Assert.Equal(20, response.Deserialize<List<CompressionController.Reading>>().Count);
    }

    [HardenedTest]
    public async Task AnOperationFavouringBrotliAnswersBrotliWhenAccepted(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/compression/brotli",
            request => request.Headers[KnownHeaders.AcceptEncoding] = "gzip, deflate, br");

        response.Assert.Ok();

        Assert.Equal("br", Coding(response));
        Assert.Equal(20, response.Deserialize<List<CompressionController.Reading>>().Count);
    }

    [HardenedTest]
    public async Task APredicateDecidesFromTheHandlersValue(ITestWebApp testWebApp) {
        var small = await testWebApp.Get("/compression/sized/2");
        var large = await testWebApp.Get("/compression/sized/5");

        small.Assert.Ok();
        large.Assert.Ok();

        Assert.Equal("", Coding(small));
        Assert.Equal("gzip", Coding(large));
        Assert.Equal(2, small.Deserialize<List<CompressionController.Reading>>().Count);
        Assert.Equal(5, large.Deserialize<List<CompressionController.Reading>>().Count);
    }

    [HardenedTest]
    public async Task AnOperationCanOptOutOfTheDefault(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/compression/never");

        response.Assert.Ok();

        Assert.Equal("", Coding(response));
        Assert.False(LooksGzip(response));
    }

    [HardenedTest]
    public async Task TheMediaTypeRuleDecidesForAnUndeclaredOperation(ITestWebApp testWebApp) {
        var text = await testWebApp.Get("/compression/text");
        var binary = await testWebApp.Get("/compression/binary");

        Assert.Equal("gzip", Coding(text));
        Assert.Equal("", Coding(binary));
        Assert.Equal(8, binary.Body.Length);
    }

    /// <summary>
    /// A HEAD runs the GET inside a counting stream, and the compression filter encodes into the
    /// counter - so the length reported is the one the GET actually sends.
    /// </summary>
    [HardenedTest]
    public async Task AHeadReportsTheCompressedLength(ITestWebApp testWebApp) {
        var get = await testWebApp.Get("/compression/readings");
        var head = await testWebApp.Request("HEAD", null, "/compression/readings");

        Assert.Equal("gzip", Coding(get));
        Assert.Equal("gzip", Coding(head));
        Assert.Equal(get.Body.Length.ToString(), head.Headers[KnownHeaders.ContentLength].ToString());
        Assert.Equal(0, head.Body.Length);
    }

    /// <summary>
    /// What the streaming guide used to say could not be done. One member around the whole
    /// stream, and the items still decode.
    /// </summary>
    [HardenedTest]
    public async Task AnNdjsonStreamIsGzipped(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/models");

        response.Assert.Ok();

        Assert.Equal("gzip", Coding(response));

        var items = new List<StreamingController.Measurement>();

        await foreach (var item in response.DeserializeAsyncEnumerable<StreamingController.Measurement>()) {
            items.Add(item);
        }

        Assert.Equal(3, items.Count);
    }

    [HardenedTest]
    public async Task AnEventStreamIsNotCompressed(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/streaming/events");

        response.Assert.Ok();

        Assert.Equal("", Coding(response));
    }

    // ---------------------------------------------------------------- requests

    [HardenedTest]
    public async Task AGzippedJsonBodyIsRead(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            GZipped("""{"sensor":"north","value":12}"""), "/compression/echo", request => {
                request.Headers[KnownHeaders.ContentType] = "application/json";
                request.Headers[KnownHeaders.ContentEncoding] = "gzip";
            });

        response.Assert.Ok();

        Assert.Equal(new CompressionController.Reading("north", 12), response.Deserialize<CompressionController.Reading>());
    }

    /// <summary>
    /// A form could not arrive compressed while the JSON deserializers did the decoding, because
    /// the form reader read the raw body. The filter decodes for every reader.
    /// </summary>
    [HardenedTest]
    public async Task AGzippedFormBodyIsRead(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            GZipped("username=ada&password=hunter2"), "/form/sign-in", request => {
                request.Headers[KnownHeaders.ContentType] = "application/x-www-form-urlencoded";
                request.Headers[KnownHeaders.ContentEncoding] = "gzip";
            });

        response.Assert.Ok();

        Assert.Equal("ada:hunter2", response.Deserialize<string>());
    }

    [HardenedTest]
    public async Task ACodingTheServerDoesNotDecodeIsA415NamingWhatItDoes(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            """{"sensor":"north","value":12}""", "/compression/echo", request => {
                request.Headers[KnownHeaders.ContentType] = "application/json";
                request.Headers[KnownHeaders.ContentEncoding] = "deflate";
            });

        Assert.Equal(415, response.StatusCode);
        Assert.Equal("gzip, br", response.Headers[KnownHeaders.AcceptEncoding].ToString());
    }

    /// <summary>
    /// The fixture caps a decoded body at 4096 bytes. A few hundred bytes of gzip past it is a
    /// 413, from inside the bind, on a request the host's own limit would have let through.
    /// </summary>
    [HardenedTest]
    public async Task ABodyDecodingPastTheCapIsA413(ITestWebApp testWebApp) {
        var oversized = "{\"sensor\":\"" + new string('x', 10_000) + "\",\"value\":1}";

        var response = await testWebApp.Post(GZipped(oversized), "/compression/echo", request => {
            request.Headers[KnownHeaders.ContentType] = "application/json";
            request.Headers[KnownHeaders.ContentEncoding] = "gzip";
        });

        Assert.Equal(413, response.StatusCode);
    }
}
