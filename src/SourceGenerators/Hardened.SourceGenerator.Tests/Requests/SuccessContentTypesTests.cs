using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Xunit;

namespace Hardened.SourceGenerator.Tests.Requests;

/// <summary>
/// The media types a described operation's successes are declared with, as the handler info
/// carries them to the 406.
/// </summary>
public class SuccessContentTypesTests
{
    private static ResponseInformationModel Response(string? produced, string? successes) =>
        new() { ProducedContentTypes = produced, SuccessContentTypes = successes };

    /// <summary>
    /// The 0.42 trial's B-19: the 406 named the produced set, which carries the failures' problem
    /// JSON beside the success's JSON.
    /// </summary>
    [Fact]
    public void SuccessesDeclaredApartAreCarried()
    {
        var carried = HandlerInfoCodeGenerator.SuccessContentTypes(
            Response("application/json,application/problem+json", "application/json")
        );

        Assert.NotNull(carried);
        Assert.Equal(["application/json"], carried);
    }

    [Fact]
    public void SuccessesDeclaredAsTheWholeProducedSetAreNotCarried()
    {
        Assert.Null(
            HandlerInfoCodeGenerator.SuccessContentTypes(
                Response("application/json", "application/json")
            )
        );
    }

    [Fact]
    public void AnOperationThatNegotiatesNothingCarriesNone()
    {
        Assert.Null(
            HandlerInfoCodeGenerator.SuccessContentTypes(
                Response(null, "application/x-amz-json-1.0")
            )
        );
    }
}
