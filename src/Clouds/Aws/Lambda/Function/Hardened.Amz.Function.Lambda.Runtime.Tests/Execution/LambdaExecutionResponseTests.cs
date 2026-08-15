using System.Text;
using Hardened.Amz.Function.Lambda.Runtime.Execution;
using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Abstract.Outputs;
using Hardened.Requests.Runtime.Headers;
using NSubstitute;

namespace Hardened.Amz.Function.Lambda.Runtime.Tests.Execution;

public class LambdaExecutionResponseTests {

    private static LambdaExecutionResponse Create(Stream? body = null) {
        return new LambdaExecutionResponse(body ?? new MemoryStream(), new HeaderCollectionStringValues());
    }

    /// <summary>
    /// A record is judged a batch success by its response status, so a filter that forgets to set
    /// one has to mean "no opinion", not "failed".
    /// </summary>
    [Fact]
    public void AFreshResponseHasNoStatus() {
        Assert.Null(Create().Status);
    }

    [Fact]
    public void AFreshResponseSerializesByDefault() {
        Assert.True(Create().ShouldSerialize);
    }

    /// <summary>
    /// <c>ResponseStarted</c> is derived from the body position rather than tracked, so anything
    /// already written to the stream counts as started.
    /// </summary>
    [Fact]
    public void AResponseWithNothingWrittenHasNotStarted() {
        Assert.False(Create().ResponseStarted);
    }

    [Fact]
    public void WritingToTheBodyStartsTheResponse() {
        var response = Create();
        var bytes = Encoding.UTF8.GetBytes("{}");

        response.Body.Write(bytes, 0, bytes.Length);

        Assert.True(response.ResponseStarted);
    }

    [Fact]
    public void TheContentTypeIsReadAndWrittenThroughTheHeaderCollection() {
        var response = Create();

        response.ContentType = "application/json";

        Assert.Equal("application/json", response.ContentType);
        Assert.Equal("application/json", response.Headers.Get(KnownHeaders.ContentType).ToString());
    }

    [Fact]
    public void CloningCarriesTheStatusAndResponseValue() {
        var response = Create();
        var value = new object();

        response.Status = 201;
        response.ResponseValue = value;
        response.IsBinary = true;
        response.ShouldCompress = true;
        response.ShouldSerialize = false;
        var output = Substitute.For<IHardenedResponseOutput>();
        Func<IExecutionContext, IHardenedResponseOutput> factory = _ => output;

        response.Output = output;
        response.OutputFactory = factory;

        var clone = response.Clone(null);

        Assert.Equal(201, clone.Status);
        Assert.Same(value, clone.ResponseValue);
        Assert.True(clone.IsBinary);
        Assert.True(clone.ShouldCompress);
        Assert.False(clone.ShouldSerialize);
        Assert.Same(output, clone.Output);
        Assert.Same(factory, clone.OutputFactory);
    }

    [Fact]
    public void CloningKeepsTheSameBodyStream() {
        var body = new MemoryStream();
        var response = Create(body);

        Assert.Same(body, ((IExecutionResponse)response.Clone()).Body);
    }

    [Fact]
    public void CloningTakesTheSuppliedHeaderCollection() {
        var headers = new HeaderCollectionStringValues();
        headers.Set("X-Test", "value");

        var clone = (LambdaExecutionResponse)Create().Clone(headers);

        Assert.Same(headers, clone.Headers);
    }

    [Fact]
    public void CloningWithoutHeadersKeepsTheOriginalCollection() {
        var response = Create();

        var clone = (LambdaExecutionResponse)response.Clone(null);

        Assert.Same(response.Headers, clone.Headers);
    }

    /// <summary>
    /// <see cref="IExecutionResponse.Headers"/> and the typed <c>Headers</c> property are two
    /// different collections on this type. Worth pinning: code reading the interface's dictionary
    /// does not see what the typed collection was given.
    /// </summary>
    [Fact]
    public void TheInterfaceHeaderDictionaryIsSeparateFromTheTypedCollection() {
        var response = Create();

        response.ContentType = "application/json";

        Assert.Empty(((IExecutionResponse)response).Headers);
    }
}
