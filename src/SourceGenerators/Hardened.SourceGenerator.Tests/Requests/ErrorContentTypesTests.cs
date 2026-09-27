using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The media types a described operation's failures are declared with, as the handler info
/// carries them to the locator.
/// </summary>
public class ErrorContentTypesTests
{
    private static ResponseInformationModel Response(string? produced, string? errors) =>
        new() { ProducedContentTypes = produced, ErrorContentTypes = errors };

    /// <summary>
    /// The case the trial found: JSON for the success, problem JSON for the failure. Negotiated
    /// within the produced set, the failure went out as the success's media type.
    /// </summary>
    [Fact]
    public void FailuresDeclaredApartAreCarried()
    {
        var carried = HandlerInfoCodeGenerator.ErrorContentTypes(
            Response("application/json,application/problem+json", "application/problem+json")
        );

        Assert.NotNull(carried);
        Assert.Equal(["application/problem+json"], carried);
    }

    /// <summary>
    /// One media type for everything negotiates the same either way, so nothing is emitted and the
    /// generated code of every such operation is unchanged.
    /// </summary>
    [Fact]
    public void FailuresDeclaredAsTheWholeProducedSetAreNotCarried()
    {
        Assert.Null(
            HandlerInfoCodeGenerator.ErrorContentTypes(
                Response("application/json", "application/json")
            )
        );
    }

    /// <summary>
    /// A dispatch protocol names one media type for the document and negotiates nothing, so its
    /// failures are written by the default serializer. A set here would send them to a media type
    /// nothing writes.
    /// </summary>
    [Fact]
    public void AnOperationThatNegotiatesNothingCarriesNone()
    {
        Assert.Null(
            HandlerInfoCodeGenerator.ErrorContentTypes(Response(null, "application/x-amz-json-1.0"))
        );
    }

    [Fact]
    public void AnOperationWhoseFailuresNothingDeclaredCarriesNone()
    {
        Assert.Null(HandlerInfoCodeGenerator.ErrorContentTypes(Response("application/json", null)));
    }
}
