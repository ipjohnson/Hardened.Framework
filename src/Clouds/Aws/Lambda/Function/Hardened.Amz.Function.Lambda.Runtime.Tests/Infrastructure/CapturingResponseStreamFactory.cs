using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Infrastructure;

/// <summary>
/// The stream-opening seam, recording what was opened and keeping every byte written.
/// </summary>
public sealed class CapturingResponseStreamFactory : IResponseStreamFactory {
    public int PlainStreams { get; private set; }

    public List<HttpResponseStreamPrelude> Preludes { get; } = [];

    public MemoryStream Target { get; } = new();

    public Stream CreateStream() {
        PlainStreams++;

        return Target;
    }

    public Stream CreateHttpStream(HttpResponseStreamPrelude prelude) {
        Preludes.Add(prelude);

        return Target;
    }
}
