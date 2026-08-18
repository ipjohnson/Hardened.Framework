using System.Collections.Generic;
using Hardened.Idl.Emitters;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The exception type a handler throws to produce a response the specification declares.
/// </summary>
/// <remarks>
/// <para>
/// The emitter was at <b>7% line coverage</b> — the eight lines that ran were the empty-loop case
/// reached incidentally while emitting whole files. Every declared error response in every fixture
/// produced code nothing had ever looked at.
/// </para>
/// <para>
/// What matters here is the two shapes. A response with no declared body is a status and nothing
/// else; a response naming a schema also gets a constructor parameter and a typed <c>Body</c>. The
/// second is where the sharp edges are — the payload cast is written as raw code, so it bypasses
/// the output context that qualifies every other type in the file.
/// </para>
/// </remarks>
public class ErrorResponseEmitterTests {

    private static OperationModel Operation(
        string methodName = "GetPet",
        string path = "/pets/{petId}",
        string httpMethod = "GET",
        params ErrorResponseModel[] errors) =>
        new() {
            OperationId = methodName,
            MethodName = methodName,
            Path = path,
            HttpMethod = httpMethod,
            ErrorResponses = new List<ErrorResponseModel>(errors)
        };

    private static string Emit(params OperationModel[] operations) =>
        EmitterHarness.Write(ns => ErrorResponseEmitter.Emit(
            ns,
            new ServiceModel { Tag = "pets", Operations = new List<OperationModel>(operations) },
            EmitterHarness.ModelsNamespace));

    private static IReadOnlyList<string> Names(params OperationModel[] operations) {
        var emitted = new List<string>();

        EmitterHarness.Write(ns => {
            foreach (var definition in ErrorResponseEmitter.Emit(
                         ns,
                         new ServiceModel { Tag = "pets", Operations = new List<OperationModel>(operations) },
                         EmitterHarness.ModelsNamespace)) {
                emitted.Add(definition.Name);
            }
        });

        return emitted;
    }

    #region naming

    /// <summary>
    /// Named for the operation as well as the status, so what a handler may throw is discoverable
    /// from the handler — and so it cannot collide with the framework's own
    /// <c>BadRequestException</c>, which a status-only name would.
    /// </summary>
    [Fact]
    public void AnExceptionIsNamedForItsOperationAndStatus() {
        Assert.Equal(
            ["GetPetNotFoundException"],
            Names(Operation(errors: new ErrorResponseModel { StatusCode = 404 })));
    }

    [Theory]
    [InlineData(400, "GetPetBadRequestException")]
    [InlineData(401, "GetPetUnauthorizedException")]
    [InlineData(402, "GetPetPaymentRequiredException")]
    [InlineData(403, "GetPetForbiddenException")]
    [InlineData(404, "GetPetNotFoundException")]
    [InlineData(405, "GetPetMethodNotAllowedException")]
    [InlineData(406, "GetPetNotAcceptableException")]
    [InlineData(408, "GetPetRequestTimeoutException")]
    [InlineData(409, "GetPetConflictException")]
    [InlineData(410, "GetPetGoneException")]
    [InlineData(412, "GetPetPreconditionFailedException")]
    [InlineData(413, "GetPetPayloadTooLargeException")]
    [InlineData(415, "GetPetUnsupportedMediaTypeException")]
    [InlineData(422, "GetPetUnprocessableEntityException")]
    [InlineData(423, "GetPetLockedException")]
    [InlineData(429, "GetPetTooManyRequestsException")]
    [InlineData(500, "GetPetInternalServerErrorException")]
    [InlineData(501, "GetPetNotImplementedException")]
    [InlineData(502, "GetPetBadGatewayException")]
    [InlineData(503, "GetPetServiceUnavailableException")]
    [InlineData(504, "GetPetGatewayTimeoutException")]
    public void EveryWellKnownStatusHasAName(int statusCode, string expected) {
        Assert.Equal(
            [expected], Names(Operation(errors: new ErrorResponseModel { StatusCode = statusCode })));
    }

    /// <summary>
    /// A status with no well-known name keeps its number. Reads badly, but a specification using
    /// 418 deserves a type as much as one using 404.
    /// </summary>
    [Theory]
    [InlineData(418, "GetPetStatus418Exception")]
    [InlineData(451, "GetPetStatus451Exception")]
    [InlineData(599, "GetPetStatus599Exception")]
    public void AnUnrecognisedStatusKeepsItsNumber(int statusCode, string expected) {
        Assert.Equal(
            [expected], Names(Operation(errors: new ErrorResponseModel { StatusCode = statusCode })));
    }

    [Fact]
    public void EveryDeclaredErrorOnAnOperationGetsAType() {
        Assert.Equal(
            ["GetPetNotFoundException", "GetPetConflictException"],
            Names(Operation(errors:
                [new ErrorResponseModel { StatusCode = 404 }, new ErrorResponseModel { StatusCode = 409 }])));
    }

    [Fact]
    public void EveryOperationInTheServiceContributes() {
        Assert.Equal(
            ["GetPetNotFoundException", "DeletePetConflictException"],
            Names(
                Operation("GetPet", errors: new ErrorResponseModel { StatusCode = 404 }),
                Operation("DeletePet", errors: new ErrorResponseModel { StatusCode = 409 })));
    }

    [Fact]
    public void AnOperationDeclaringNoErrorsEmitsNothing() {
        Assert.Empty(Names(Operation()));
    }

    [Fact]
    public void AServiceWithNoOperationsEmitsNothing() {
        EmitterHarness.Write(ns => Assert.Empty(
            ErrorResponseEmitter.Emit(
                ns, new ServiceModel { Tag = "pets" }, EmitterHarness.ModelsNamespace)));
    }

    #endregion

    #region shape

    /// <summary>
    /// Partial, so a consumer can add to it, and derived from the framework's status exception so
    /// the pipeline turns it into a response rather than a 500.
    /// </summary>
    [Fact]
    public void TheExceptionIsAPublicPartialStatusCodeException() {
        var output = Emit(Operation(errors: new ErrorResponseModel { StatusCode = 404 }));

        Assert.Contains("public partial class GetPetNotFoundException", output);
        Assert.Contains("StatusCodeException", output);
    }

    /// <summary>
    /// No declared body means a status and nothing else — no constructor parameter, no
    /// <c>Body</c>.
    /// </summary>
    [Fact]
    public void AResponseWithNoBodyTakesNoConstructorArgument() {
        var output = Emit(Operation(errors: new ErrorResponseModel { StatusCode = 404 }));

        Assert.Contains("base(404)", output);
        Assert.DoesNotContain("Body", output);
    }

    [Fact]
    public void AResponseWithABodyPassesItToTheBase() {
        var output = Emit(Operation(errors:
            new ErrorResponseModel { StatusCode = 404, Ref = "#/components/schemas/Error" }));

        Assert.Contains("base(404, value)", output);
    }

    /// <summary>
    /// Typed access to the body, which the base can only offer as <c>object</c>.
    /// </summary>
    [Fact]
    public void AResponseWithABodyExposesItTyped() {
        var output = Emit(Operation(errors:
            new ErrorResponseModel { StatusCode = 404, Ref = "#/components/schemas/Error" }));

        Assert.Contains("Error Body", output);
    }

    /// <summary>
    /// <b>Named <c>Body</c> rather than hiding the base's <c>Value</c>.</b> A reader seeing
    /// <c>Value</c> on the derived type would have no way to tell it was not the one they knew
    /// about.
    /// </summary>
    [Fact]
    public void TheTypedAccessorDoesNotHideTheBasesValue() {
        var output = Emit(Operation(errors:
            new ErrorResponseModel { StatusCode = 404, Ref = "#/components/schemas/Error" }));

        Assert.DoesNotContain("new ", output);
        Assert.Contains("Value!", output);
    }

    /// <summary>
    /// The cast is written as raw code, so it bypasses the output context that qualifies every
    /// other type in the file. It has to carry the global-qualified name itself, or it binds to a
    /// consumer type of the same name.
    /// </summary>
    [Fact]
    public void TheBodyCastIsGlobalQualified() {
        var output = Emit(Operation(errors:
            new ErrorResponseModel { StatusCode = 404, Ref = "#/components/schemas/Error" }));

        Assert.Contains($"(global::{EmitterHarness.ModelsNamespace}.Error)Value!", output);
    }

    [Fact]
    public void ARefIsPascalCasedIntoATypeName() {
        var output = Emit(Operation(errors:
            new ErrorResponseModel { StatusCode = 409, Ref = "#/components/schemas/conflict_detail" }));

        Assert.Contains("ConflictDetail Body", output);
    }

    #endregion

    #region documentation

    [Fact]
    public void TheDeclaredDescriptionBecomesTheDocComment() {
        var output = Emit(Operation(errors: new ErrorResponseModel {
            StatusCode = 404, Description = "No pet with that identifier."
        }));

        Assert.Contains("No pet with that identifier.", output);
    }

    /// <summary>
    /// A response the document did not describe still gets a comment, built from what is known.
    /// </summary>
    [Fact]
    public void AResponseWithNoDescriptionGetsAGeneratedOne() {
        var output = Emit(Operation(errors: new ErrorResponseModel { StatusCode = 404 }));

        Assert.Contains("The 404 response declared for GET /pets/{petId}.", output);
    }

    #endregion
}
