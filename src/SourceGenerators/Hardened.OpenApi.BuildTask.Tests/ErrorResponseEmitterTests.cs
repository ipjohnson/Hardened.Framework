using System.Collections.Generic;
using Hardened.Idl.Emitters;
using Hardened.Generation.Models;
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
/// <para>
/// <b>What is emitted at all is not decided here.</b> Most declared errors bind to a shipped
/// response and reach this emitter never — <see cref="ShippedResponsesTests"/> covers that, and
/// <see cref="NameAllocatorTests"/> covers what the ones that survive are called. This takes a list
/// and writes it.
/// </para>
/// </remarks>
public class ErrorResponseEmitterTests {

    /// <summary>
    /// One error, with the name the allocator would have put on it already there.
    /// </summary>
    /// <remarks>
    /// Set rather than allocated, because the allocator needs a whole document to arbitrate
    /// against and this emitter needs only the answer. The end-to-end path — a description in,
    /// a named type out — is in <see cref="NameAllocatorTests"/>.
    /// </remarks>
    private static ErrorResponseModel Error(
        int statusCode = 404, string? bodyRef = null, string? description = null,
        string? name = null, string exceptionTypeName = "NotFoundException") =>
        new() {
            StatusCode = statusCode,
            Ref = bodyRef,
            Description = description,
            Name = name,
            ExceptionTypeName = exceptionTypeName
        };

    private static string Emit(params ErrorResponseModel[] errors) =>
        EmitterHarness.Write(ns => ErrorResponseEmitter.Emit(
            ns, new List<ErrorResponseModel>(errors), EmitterHarness.ModelsNamespace));

    private static IReadOnlyList<string> Names(params ErrorResponseModel[] errors) {
        var emitted = new List<string>();

        EmitterHarness.Write(ns => {
            foreach (var definition in ErrorResponseEmitter.Emit(
                         ns, new List<ErrorResponseModel>(errors),
                         EmitterHarness.ModelsNamespace)) {
                emitted.Add(definition.Name);
            }
        });

        return emitted;
    }

    #region naming

    /// <summary>
    /// Named by the allocator, and nothing here re-derives it. The name used to be composed from
    /// the operation and the status at the point of emission, which is what made
    /// <c>GetPetNotFoundException</c> and <c>GetPetLabelNotFoundException</c> two names for one
    /// class.
    /// </summary>
    [Fact]
    public void AnExceptionTakesTheNameOnTheModel() {
        Assert.Equal(
            ["AccountNotFoundException"],
            Names(Error(name: "AccountNotFound", exceptionTypeName: "AccountNotFoundException")));
    }

    [Fact]
    public void EveryErrorInTheSetGetsAType() {
        Assert.Equal(
            ["NotFoundProblemException", "ConflictProblemException"],
            Names(
                Error(404, exceptionTypeName: "NotFoundProblemException"),
                Error(409, exceptionTypeName: "ConflictProblemException")));
    }

    [Fact]
    public void AnEmptySetEmitsNothing() {
        Assert.Empty(Names());
    }

    #endregion

    #region shape

    /// <summary>
    /// Partial, so a consumer can add to it, and derived from the framework's status exception so
    /// the pipeline turns it into a response rather than a 500.
    /// </summary>
    [Fact]
    public void TheExceptionIsAPublicPartialStatusCodeException() {
        var output = Emit(Error());

        Assert.Contains("public partial class NotFoundException", output);
        Assert.Contains("StatusCodeException", output);
    }

    /// <summary>
    /// No declared body means a status and nothing else — no constructor parameter, no
    /// <c>Body</c>.
    /// </summary>
    [Fact]
    public void AResponseWithNoBodyTakesNoConstructorArgument() {
        var output = Emit(Error());

        Assert.Contains("base(404)", output);
        Assert.DoesNotContain("Body", output);
    }

    [Fact]
    public void AResponseWithABodyPassesItToTheBase() {
        var output = Emit(Error(bodyRef: "#/components/schemas/Error"));

        Assert.Contains("base(404, value)", output);
    }

    /// <summary>
    /// Typed access to the body, which the base can only offer as <c>object</c>.
    /// </summary>
    [Fact]
    public void AResponseWithABodyExposesItTyped() {
        var output = Emit(Error(bodyRef: "#/components/schemas/Error"));

        Assert.Contains("Error Body", output);
    }

    /// <summary>
    /// <b>Named <c>Body</c> rather than hiding the base's <c>Value</c>.</b> A reader seeing
    /// <c>Value</c> on the derived type would have no way to tell it was not the one they knew
    /// about.
    /// </summary>
    [Fact]
    public void TheTypedAccessorDoesNotHideTheBasesValue() {
        var output = Emit(Error(bodyRef: "#/components/schemas/Error"));

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
        var output = Emit(Error(bodyRef: "#/components/schemas/Error"));

        Assert.Contains($"(global::{EmitterHarness.ModelsNamespace}.Error)Value!", output);
    }

    [Fact]
    public void ARefIsPascalCasedIntoATypeName() {
        var output = Emit(Error(409, "#/components/schemas/conflict_detail",
            exceptionTypeName: "ConflictConflictDetailException"));

        Assert.Contains("ConflictDetail Body", output);
    }

    #endregion

    #region documentation

    [Fact]
    public void TheDeclaredDescriptionBecomesTheDocComment() {
        var output = Emit(Error(description: "No pet with that identifier."));

        Assert.Contains("No pet with that identifier.", output);
    }

    /// <summary>
    /// A response the document did not describe still gets a comment, built from what is known.
    /// </summary>
    /// <remarks>
    /// No operation in it, and there cannot be one: the type is shared by every operation that
    /// declares the error, which is the whole reason it is emitted once.
    /// </remarks>
    [Fact]
    public void AResponseWithNoDescriptionGetsAGeneratedOne() {
        Assert.Contains(
            "The 404 response the description declares.", Emit(Error()));
    }

    [Fact]
    public void ANamedErrorWithNoDescriptionSaysWhatItWasCalled() {
        Assert.Contains(
            "The 400 response the description declares as 'AccountNotFound'.",
            Emit(Error(400, name: "AccountNotFound",
                exceptionTypeName: "AccountNotFoundException")));
    }

    #endregion
}
