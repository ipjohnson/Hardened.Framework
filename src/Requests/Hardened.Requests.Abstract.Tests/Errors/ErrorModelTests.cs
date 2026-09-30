using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Abstract.Responses;
using Hardened.Requests.Abstract.Serializer;
using Xunit;

namespace Hardened.Requests.Abstract.Tests.Errors;

/// <summary>
/// The problem document every refusal the framework raises is sent as.
/// </summary>
public class ErrorModelTests
{
    [Fact]
    public void ForFillsEveryMemberFromTheStatus()
    {
        var model = ErrorModel.For(415, "This route does not read text/plain.");

        Assert.Equal("urn:hardened:problem:unsupported-media-type", model.Type);
        Assert.Equal("Unsupported Media Type", model.Title);
        Assert.Equal(415, model.Status);
        Assert.Equal("This route does not read text/plain.", model.Detail);
    }

    [Fact]
    public void ADetailIsOptional()
    {
        Assert.Null(ErrorModel.For(404).Detail);
    }

    [Fact]
    public void ItIsAProblemDocument()
    {
        IProblemDetails problem = ErrorModel.For(403);

        Assert.Equal(ErrorModel.TypePrefix + "forbidden", problem.Type);
        Assert.True(problem.HasBody);
    }

    [Theory]
    [InlineData(400, "urn:hardened:problem:bad-request")]
    [InlineData(405, "urn:hardened:problem:method-not-allowed")]
    [InlineData(413, "urn:hardened:problem:content-too-large")]
    [InlineData(500, "urn:hardened:problem:internal-server-error")]
    [InlineData(505, "urn:hardened:problem:http-version-not-supported")]
    public void TheTypeIsTheReasonPhraseInLowerCaseWithHyphens(int status, string type)
    {
        Assert.Equal(type, ErrorModel.TypeFor(status));
    }

    /// <summary>
    /// The name of the record a handler returns for 429, rather than its reason phrase.
    /// </summary>
    [Fact]
    public void TooManyRequestsIsRateLimited()
    {
        Assert.Equal("urn:hardened:problem:rate-limited", ErrorModel.TypeFor(429));
        Assert.Equal("Too Many Requests", ErrorModel.For(429).Title);
    }

    /// <summary>
    /// A status below 400 is not a problem, and one without a reason phrase has no name to take.
    /// RFC 9457 gives <c>about:blank</c> for both.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(304)]
    [InlineData(418)]
    [InlineData(499)]
    public void AStatusWithNoProblemNameIsAboutBlank(int status)
    {
        Assert.Equal("about:blank", ErrorModel.TypeFor(status));
    }

    [Fact]
    public void AStatusWithoutAReasonPhraseIsTitledWithItsNumber()
    {
        var model = ErrorModel.For(499);

        Assert.Equal("about:blank", model.Type);
        Assert.Equal("499", model.Title);
    }

    [Fact]
    public void ANewInstanceIsAboutBlank()
    {
        var model = new ErrorModel();

        Assert.Equal("about:blank", model.Type);
        Assert.Equal("", model.Title);
        Assert.Equal(0, model.Status);
        Assert.Null(model.Detail);
    }

    [Fact]
    public void A406NamesWhatTheOperationProduces()
    {
        var exception = new NotAcceptableException(["application/json", "text/plain"]);

        var model = Assert.IsType<ErrorModel>(exception.Value);

        Assert.Equal(406, exception.StatusCode);
        Assert.Equal("urn:hardened:problem:not-acceptable", model.Type);
        Assert.Equal("This operation produces application/json, text/plain.", model.Detail);
    }
}
