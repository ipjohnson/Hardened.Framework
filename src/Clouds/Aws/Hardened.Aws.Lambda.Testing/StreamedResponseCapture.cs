using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Aws.Lambda.Runtime.Streaming;

namespace Hardened.Aws.Lambda.Testing;

/// <summary>
/// The stream a function in <c>RESPONSE_STREAM</c> mode wrote, kept so a test can read it.
/// </summary>
/// <remarks>
/// <para>
/// A streamed invocation answers nothing through its output stream - it opens a Lambda response
/// stream at the first byte and writes there, and the bootstrap ignores what <c>Invoke</c> returns
/// once one has been created. So a test host that only reads the output stream sees an empty
/// response for every streamed request, which is why <c>[LambdaWebTesting]</c> could not run in
/// stream mode at all.
/// </para>
/// <para>
/// <see cref="IResponseStreamFactory"/> is the seam that makes this reachable.
/// <c>LambdaResponseStreamFactory</c> is static and its setter is internal to the AWS packages, so
/// nothing built on it can be driven from a test; the runtime opens every stream through the
/// interface instead, and this is the test's implementation of it.
/// </para>
/// <para>
/// <b>One invocation at a time.</b> <c>LambdaWebHost</c> resets this before each request and reads
/// it after, so two requests sent concurrently from one test would interleave into one capture.
/// Sequential sends - which is what <c>ITestWebApp</c> and a typed client both do - are unaffected.
/// </para>
/// </remarks>
public sealed class StreamedResponseCapture : IResponseStreamFactory {
    private MemoryStream _body = new();

    /// <summary>The prelude the stream opened with, or null where nothing opened one.</summary>
    public HttpResponseStreamPrelude? Prelude { get; private set; }

    /// <summary>Whether this invocation opened a Lambda response stream at all.</summary>
    public bool Opened => Prelude != null || PlainStreams > 0;

    /// <summary>
    /// Streams opened with no prelude, which is what a function invoked through the Lambda API
    /// rather than a function URL gets.
    /// </summary>
    public int PlainStreams { get; private set; }

    /// <summary>Every byte written to the stream this invocation opened.</summary>
    public byte[] Body => _body.ToArray();

    public Stream CreateStream() {
        PlainStreams++;

        return _body;
    }

    public Stream CreateHttpStream(HttpResponseStreamPrelude prelude) {
        Prelude = prelude;

        return _body;
    }

    /// <summary>
    /// Clears what the previous invocation wrote.
    /// </summary>
    /// <remarks>
    /// A new stream rather than <c>SetLength(0)</c>, because the runtime's <c>ResponseStream</c>
    /// keeps whatever it was handed for the life of the invocation and a test asserting on the
    /// previous body should not see it grow.
    /// </remarks>
    internal void Reset() {
        _body = new MemoryStream();
        Prelude = null;
        PlainStreams = 0;
    }
}
