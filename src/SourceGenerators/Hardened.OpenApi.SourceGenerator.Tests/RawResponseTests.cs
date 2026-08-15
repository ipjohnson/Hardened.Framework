using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// Responses a spec declares as something other than JSON, and the opt-in to a byte[] signature.
///
/// <para>
/// The parser has carried the media type the signature was built from for a while; nothing read it.
/// An operation declaring <c>text/plain</c> generated a handler whose result went through the JSON
/// serializer, so a plain-text endpoint described in a specification answered with a quoted JSON
/// string under <c>application/json</c> — the one thing the document said it would not do.
/// </para>
/// </summary>
public class RawResponseTests {

    private const string PlainText =
        """
        openapi: "3.0.0"
        info: { title: Text, version: "1.0" }
        paths:
          /greeting:
            get:
              tags: [Greeting]
              operationId: getGreeting
              responses:
                '200':
                  description: A greeting
                  content:
                    text/plain:
                      schema: { type: string }
        """;

    private const string PlainTextRawBytes =
        """
        openapi: "3.0.0"
        info: { title: Text, version: "1.0" }
        paths:
          /greeting:
            get:
              tags: [Greeting]
              operationId: getGreeting
              x-hardened-raw-bytes: true
              responses:
                '200':
                  description: A greeting
                  content:
                    text/plain:
                      schema: { type: string }
        """;
    /// <summary>
    /// The default stays string, because that is what <c>type: string</c> means and what someone
    /// reading the document expects the handler to return.
    /// </summary>
    [Fact]
    public void ATextResponseIsAStringUnlessAskedOtherwise() {
        var generated = OpenApiGenerator.Run(PlainText).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("Task<string> GetGreeting()", generated);
        Assert.DoesNotContain("Task<byte[]>", generated);
    }

    /// <summary>
    /// x-hardened-raw-bytes opts into byte[], which RawOutputHelper writes straight to the body
    /// where a string is UTF-8 encoded into a fresh array per request.
    /// </summary>
    [Fact]
    public void RawBytesOptsTheSignatureIntoByteArray() {
        var generated = OpenApiGenerator.Run(PlainTextRawBytes).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("Task<byte[]> GetGreeting()", generated);
    }
    [Fact]
    public void AByteArrayResponseCompilesForItsConsumer() {
        OpenApiGenerator.Run(
                PlainTextRawBytes,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class GreetingServiceImpl : IGreetingService {
                        private static readonly byte[] Payload = "Hello"u8.ToArray();

                        public Task<byte[]> GetGreeting() => Task.FromResult(Payload);
                    }
                    """))
            .AssertNoErrors();
    }
}
