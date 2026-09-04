using Amazon.Lambda.Core.ResponseStreaming;
using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Shared.Lambda.Runtime.Streaming;

/// <summary>
/// Opens the stream a response is written to. Creating it is what opts the invocation into
/// streaming.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LambdaResponseStreamFactory"/> is static and its setter is internal to the AWS
/// packages, so nothing built on it can be exercised outside a real invocation. This is the seam:
/// the hosts open every stream through it, tests capture the prelude and the bytes through it, and
/// the local harness supplies one that writes to the ASP.NET response.
/// </para>
/// </remarks>
public interface IResponseStreamFactory {
    /// <summary>
    /// A stream with no prelude, for a function invoked through the Lambda API.
    /// </summary>
    Stream CreateStream();

    /// <summary>
    /// A stream that opens with the HTTP prelude, for a function URL in <c>RESPONSE_STREAM</c> mode.
    /// </summary>
    Stream CreateHttpStream(HttpResponseStreamPrelude prelude);
}

/// <summary>
/// The AWS bootstrap's stream. Valid only inside an invocation
/// <c>Amazon.Lambda.RuntimeSupport</c> is running; anywhere else the factory reports itself
/// uninitialised.
/// </summary>
[SingletonService(Using = RegistrationType.Try)]
public sealed class RuntimeResponseStreamFactory : IResponseStreamFactory {
    public Stream CreateStream() => LambdaResponseStreamFactory.CreateStream();

    public Stream CreateHttpStream(HttpResponseStreamPrelude prelude) =>
        LambdaResponseStreamFactory.CreateHttpStream(prelude);
}
