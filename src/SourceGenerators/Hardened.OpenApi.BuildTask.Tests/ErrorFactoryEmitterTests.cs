using System.Collections.Generic;
using Hardened.Idl.Emitters;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// <c>AsException()</c> on the body a generated error carries.
/// </summary>
/// <remarks>
/// <para>
/// The stutter it removes is Smithy's: an error is a named shape, so the exception and the payload
/// record are named after the same thing and the throw reads
/// <c>new AccountNotFoundException(new AccountNotFound("no account"))</c>.
/// </para>
/// <para>
/// The condition is what the tests below are mostly about. Two errors over one schema would be two
/// overloads with identical signatures, and CS0111 in generated code is a failure nobody can open
/// and fix.
/// </para>
/// </remarks>
public class ErrorFactoryEmitterTests {

    private const string SpecFileName = "bank";

    private static ErrorResponseModel Error(
        int statusCode, string? bodyRef, string exceptionTypeName) =>
        new() {
            StatusCode = statusCode,
            Ref = bodyRef,
            ExceptionTypeName = exceptionTypeName
        };

    private static string Emit(params ErrorResponseModel[] errors) =>
        EmitterHarness.Write(ns => ErrorFactoryEmitter.Emit(
            ns, new List<ErrorResponseModel>(errors), EmitterHarness.ModelsNamespace,
            SpecFileName));

    private static bool Emitted(params ErrorResponseModel[] errors) {
        var any = false;

        EmitterHarness.Write(ns => any = ErrorFactoryEmitter.Emit(
            ns, new List<ErrorResponseModel>(errors), EmitterHarness.ModelsNamespace,
            SpecFileName) != null);

        return any;
    }

    #region the shorthand

    /// <summary>
    /// An extension, so the exception is inferred from the body rather than written out beside it.
    /// The same shape <c>ResponseExceptionExtensions.AsException</c> takes for the shipped records,
    /// and the same verb.
    /// </summary>
    [Fact]
    public void ThePayloadGetsAnAsExceptionExtension() {
        var output = Emit(Error(
            400, "#/components/schemas/AccountNotFound", "AccountNotFoundException"));

        Assert.Contains(
            "AccountNotFoundException AsException(this AccountNotFound body) => new(body);",
            output);
    }

    /// <summary>
    /// In a static class, because an extension method has nowhere else to live, and named for the
    /// file so <c>NameAllocator</c> can reserve it against a schema of the same name.
    /// </summary>
    [Fact]
    public void TheHolderIsAStaticClassNamedForTheFile() {
        var output = Emit(Error(
            400, "#/components/schemas/AccountNotFound", "AccountNotFoundException"));

        Assert.Contains("public static class BankErrors", output);
        Assert.Equal("BankErrors", ErrorFactoryEmitter.HolderName(SpecFileName));
    }

    [Fact]
    public void OnePerDeclaredErrorThatHasAPayload() {
        var output = Emit(
            Error(404, "#/components/schemas/PetNotFound", "PetNotFoundException"),
            Error(429, "#/components/schemas/Throttled", "ThrottledException"));

        Assert.Contains("AsException(this PetNotFound body)", output);
        Assert.Contains("AsException(this Throttled body)", output);
    }

    #endregion

    #region where it is not emitted

    /// <summary>
    /// Two errors over one schema would be two overloads with identical signatures. An OpenAPI
    /// author can write that - <c>PetMissing</c> and <c>PetLocked</c> both carrying
    /// <c>ApiError</c> - and there is no single exception an <c>ApiError</c> means.
    /// </summary>
    [Fact]
    public void APayloadTwoErrorsShareGetsNothing() {
        Assert.False(Emitted(
            Error(404, "#/components/schemas/ApiError", "PetMissingException"),
            Error(409, "#/components/schemas/ApiError", "PetLockedException")));
    }

    /// <summary>
    /// And a third over the same schema does not put it back, which a set that removed rather than
    /// marked would do.
    /// </summary>
    [Fact]
    public void AThirdErrorOverThatPayloadDoesNotPutItBack() {
        Assert.False(Emitted(
            Error(404, "#/components/schemas/ApiError", "PetMissingException"),
            Error(409, "#/components/schemas/ApiError", "PetLockedException"),
            Error(410, "#/components/schemas/ApiError", "PetGoneException")));
    }

    /// <summary>
    /// The unambiguous ones in the same document still get theirs.
    /// </summary>
    [Fact]
    public void OnlyTheSharedPayloadIsSkipped() {
        var output = Emit(
            Error(404, "#/components/schemas/ApiError", "PetMissingException"),
            Error(409, "#/components/schemas/ApiError", "PetLockedException"),
            Error(429, "#/components/schemas/Throttled", "ThrottledException"));

        Assert.Contains("AsException(this Throttled body)", output);
        Assert.DoesNotContain("ApiError body", output);
    }

    /// <summary>
    /// A response with no declared body has no payload to hang this on, and
    /// <c>new DrainingException()</c> already names its type once.
    /// </summary>
    [Fact]
    public void AnErrorWithNoBodyGetsNothing() {
        Assert.False(Emitted(Error(503, null, "DrainingException")));
    }

    [Fact]
    public void AnEmptySetEmitsNoHolder() {
        Assert.False(Emitted());
    }

    #endregion
}
