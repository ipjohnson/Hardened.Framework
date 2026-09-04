using Amazon.Lambda.Core.ResponseStreaming;
using Hardened.Amz.Shared.Lambda.Runtime.Streaming;
using Xunit;

namespace Hardened.Amz.Shared.Lambda.Runtime.Tests.Streaming;

/// <summary>
/// The default seam is the AWS factory, which only the bootstrap can initialise. Outside an
/// invocation it says so rather than handing back a stream that goes nowhere - which is the
/// reason the seam exists, and why tests and the harness substitute it.
/// </summary>
public class RuntimeResponseStreamFactoryTests {

    [Fact]
    public void OutsideABootstrapInvocationAPlainStreamCannotBeOpened() {
        var failure = Assert.Throws<InvalidOperationException>(() => new RuntimeResponseStreamFactory().CreateStream());

        Assert.Contains("not initialized", failure.Message);
    }

    [Fact]
    public void OutsideABootstrapInvocationAnHttpStreamCannotBeOpened() {
        var failure = Assert.Throws<InvalidOperationException>(
            () => new RuntimeResponseStreamFactory().CreateHttpStream(new HttpResponseStreamPrelude()));

        Assert.Contains("not initialized", failure.Message);
    }
}
