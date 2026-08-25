using System.Collections.Generic;
using Hardened.Idl.Emitters;
using Hardened.Generation.Models;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// The named union a specification-first operation returns, and its per-status case types.
/// </summary>
/// <remarks>
/// <para>
/// The sibling of <c>ErrorResponseEmitterTests</c>. That emitter expresses a declared error by
/// having the implementation throw; this one puts the whole response set in the return type, and
/// exactly one of the two runs.
/// </para>
/// <para>
/// The shape under test is the C# basic union pattern - a public single-parameter constructor per
/// case and a public <c>object? Value</c> - because that is what the code-first selector matches
/// structurally. Nothing in the compiler enforces it here either, so getting it wrong would produce
/// a type that compiles and that the dispatch generator does not recognise.
/// </para>
/// </remarks>
public class UnionResponseEmitterTests {

    private static OperationModel Operation(
        string methodName = "GetPet",
        string? responseRef = "#/components/schemas/Pet",
        params ErrorResponseModel[] errors) =>
        new() {
            OperationId = methodName,
            MethodName = methodName,
            Path = "/pets/{petId}",
            HttpMethod = "GET",
            ResponseRef = responseRef,
            ErrorResponses = new List<ErrorResponseModel>(errors)
        };

    private static ErrorResponseModel Error(int status, string? schemaRef) =>
        new() { StatusCode = status, Ref = schemaRef };

    private static string Emit(params OperationModel[] operations) =>
        Emit(asLanguageUnion: false, operations);

    private static string Emit(bool asLanguageUnion, params OperationModel[] operations) =>
        EmitterHarness.Write(ns => UnionResponseEmitter.Emit(
            ns,
            new ServiceModel { Tag = "pets", Operations = new List<OperationModel>(operations) },
            EmitterHarness.ModelsNamespace,
            asLanguageUnion));

    #region the case types

    /// <summary>
    /// One per declared status, named for the operation it belongs to - so what a handler may
    /// return is discoverable from the handler, and two operations declaring the same status do not
    /// collide.
    /// </summary>
    [Fact]
    public void OneCaseTypePerDeclaredStatus() {
        var emitted = Emit(Operation(
            errors: [Error(404, "#/components/schemas/ApiError"), Error(409, "#/components/schemas/ApiError")]));

        Assert.Contains("GetPetNotFound", emitted);
        Assert.Contains("GetPetConflict", emitted);
    }

    /// <summary>
    /// The reason the wrappers exist at all. The repo's own fixture declares 404 and 409 both
    /// referencing ApiError, so the unwrapped shape would be two identical conversions - CS0457 at
    /// the point of use.
    /// </summary>
    [Fact]
    public void TwoStatusesSharingASchemaBecomeTwoDistinctCaseTypes() {
        var emitted = Emit(Operation(
            errors: [Error(404, "#/components/schemas/ApiError"), Error(409, "#/components/schemas/ApiError")]));

        Assert.Contains("GetPetResponse(Test.Api.Models.GetPetNotFound value)", emitted);
        Assert.Contains("GetPetResponse(Test.Api.Models.GetPetConflict value)", emitted);
    }

    /// <summary>
    /// A status declaring no body is a case carrying nothing, which no <c>Response&lt;T&gt;</c>
    /// position can express and which is why the specification-first path generates its container.
    /// </summary>
    [Fact]
    public void AStatusWithNoBodyBecomesACaseTypeCarryingNothing() {
        var emitted = Emit(Operation(errors: [Error(503, null)]));

        Assert.Contains("GetPetServiceUnavailable", emitted);
        Assert.DoesNotContain("GetPetServiceUnavailable(", emitted);
    }

    /// <summary>
    /// Each carries its status as <c>[HttpStatus]</c>, which is how the dispatch generator resolves
    /// it - the specification is not there to read by then. It is the same attribute a hand-written
    /// response type carries, so one status resolution serves both front ends.
    /// </summary>
    [Fact]
    public void EachCaseTypeCarriesItsStatusAsAnAttribute() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("HttpStatus(404)", emitted);
    }

    /// <summary>
    /// Sealed, because a case type assignable to another case in the same set has no unambiguous
    /// match order.
    /// </summary>
    [Fact]
    public void CaseTypesAreSealedRecords() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("public sealed record GetPetNotFound", emitted);
    }

    #endregion

    #region the container

    /// <summary>
    /// Named for the operation, and the only definition of that scheme - the interface emitter asks
    /// this one rather than deriving it a second time.
    /// </summary>
    [Fact]
    public void TheContainerIsNamedForTheOperation() {
        Assert.Equal("GetPetResponse", ResponseSetPlan.ContainerName(Operation()));
    }

    /// <summary>
    /// The basic union pattern, which is what the code-first selector matches on. A container
    /// missing either half is a type the dispatch generator does not recognise.
    /// </summary>
    [Fact]
    public void TheContainerMatchesTheBasicUnionPattern() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("public struct GetPetResponse", emitted);
        Assert.Contains("object? Value", emitted);
        Assert.Contains("public GetPetResponse(Test.Api.Models.Pet value)", emitted);
        Assert.Contains("public GetPetResponse(Test.Api.Models.GetPetNotFound value)", emitted);
    }

    /// <summary>
    /// One conversion per case, so a handler returns the bare value rather than wrapping it.
    /// </summary>
    [Fact]
    public void TheContainerConvertsFromEveryCase() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("implicit operator GetPetResponse(Test.Api.Models.Pet value)", emitted);
        Assert.Contains("implicit operator GetPetResponse(Test.Api.Models.GetPetNotFound value)", emitted);
    }

    /// <summary>
    /// The success case is the operation's own response type rather than a wrapper, so a handler
    /// returns the pet it already had.
    /// </summary>
    [Fact]
    public void TheSuccessCaseIsNotWrapped() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.DoesNotContain("GetPetOk", emitted);
    }

    /// <summary>
    /// An operation declaring no body still gets a union of its declared errors - a handler that can
    /// only fail or return nothing is legal.
    /// </summary>
    [Fact]
    public void AnOperationWithNoSuccessBodyStillGetsAContainer() {
        var emitted = Emit(Operation(responseRef: null, errors: [Error(404, null)]));

        Assert.Contains("public struct GetPetResponse", emitted);
        Assert.Contains("public GetPetResponse(Test.Api.Models.GetPetNotFound value)", emitted);
    }

    #endregion

    #region the language union

    /// <summary>
    /// Union mode declares the container with the keyword. Every member the struct spells out - the
    /// constructors, the conversions, Value - the compiler synthesises from the case list, which is
    /// why the declaration is the whole of it.
    /// </summary>
    [Fact]
    public void UnionModeDeclaresTheContainerWithTheKeyword() {
        var emitted = Emit(
            asLanguageUnion: true,
            Operation(errors: [Error(404, "#/components/schemas/ApiError"), Error(503, null)]));

        Assert.Contains(
            "public union GetPetResponse(Pet, GetPetNotFound, GetPetServiceUnavailable);", emitted);

        Assert.DoesNotContain("public struct GetPetResponse", emitted);
        Assert.DoesNotContain("implicit operator GetPetResponse", emitted);
    }

    /// <summary>
    /// The case types are emitted byte-identical in both modes, which is what makes moving between
    /// them a container swap rather than a migration.
    /// </summary>
    [Fact]
    public void TheCaseTypesAreIdenticalInBothModes() {
        var operation = Operation(errors: [Error(404, "#/components/schemas/ApiError"), Error(503, null)]);

        var asStruct = Emit(asLanguageUnion: false, operation);
        var asUnion = Emit(asLanguageUnion: true, operation);

        foreach (var line in new[] {
                     "public sealed record GetPetNotFound",
                     "HttpStatus(404)",
                     "public sealed record GetPetServiceUnavailable",
                     "HttpStatus(503)"
                 }) {
            Assert.Contains(line, asStruct);
            Assert.Contains(line, asUnion);
        }
    }

    /// <summary>
    /// And the signature is the same type name either way, so switching modes does not rewrite the
    /// interface.
    /// </summary>
    [Fact]
    public void BothUnionModesReturnTheSameTypeName() {
        var operation = Operation(errors: [Error(404, "#/components/schemas/ApiError")]);

        Assert.Equal(
            Argument(ServiceInterfaceEmitter.GetReturnType(
                operation, EmitterHarness.ModelsNamespace, SpecResponseModel.Response)),
            Argument(ServiceInterfaceEmitter.GetReturnType(
                operation, EmitterHarness.ModelsNamespace, SpecResponseModel.Union)));
    }

    #endregion

    #region the signature

    /// <summary>
    /// The whole point of the mode: the interface returns the union rather than the bare payload.
    /// </summary>
    [Fact]
    public void ResponseModeChangesTheReturnType() {
        var operation = Operation(errors: [Error(404, "#/components/schemas/ApiError")]);

        var standard = ServiceInterfaceEmitter.GetReturnType(
            operation, EmitterHarness.ModelsNamespace, SpecResponseModel.Standard);

        var response = ServiceInterfaceEmitter.GetReturnType(
            operation, EmitterHarness.ModelsNamespace, SpecResponseModel.Response);

        // Both are Task<T>; what differs is the argument, so the name alone says nothing.
        Assert.Contains("Pet", Argument(standard));
        Assert.DoesNotContain("Response", Argument(standard));
        Assert.Contains("GetPetResponse", Argument(response));
    }

    /// <summary>
    /// An operation that declares no errors has no response set to state, so its signature is
    /// unchanged - which keeps the mode from rewriting interfaces it has nothing to add to.
    /// </summary>
    [Fact]
    public void AnOperationWithNoDeclaredErrorsKeepsItsSignature() {
        var operation = Operation();

        var response = ServiceInterfaceEmitter.GetReturnType(
            operation, EmitterHarness.ModelsNamespace, SpecResponseModel.Response);

        Assert.DoesNotContain("GetPetResponse", Argument(response));
    }

    /// <summary>The single type argument of a <c>Task&lt;T&gt;</c>, or the type's own name.</summary>
    private static string Argument(CSharpAuthor.ITypeDefinition type) =>
        type is CSharpAuthor.GenericTypeDefinition generic && generic.TypeArguments.Count > 0
            ? generic.TypeArguments[0].Name
            : type.Name;

    /// <summary>
    /// A streamed body is many responses rather than one of several, and raw bytes is a payload the
    /// application already holds encoded. Neither is a response set, and neither may be swallowed by
    /// one.
    /// </summary>
    [Fact]
    public void StreamedAndRawResponsesAreNotResponseSets() {
        var streamed = Operation(errors: [Error(404, null)]);
        streamed.ItemSchemaRef = "#/components/schemas/Pet";

        var raw = Operation(errors: [Error(404, null)]);
        raw.RawBytesResponse = true;

        Assert.DoesNotContain(
            "GetPetResponse",
            Argument(ServiceInterfaceEmitter.GetReturnType(
                streamed, EmitterHarness.ModelsNamespace, SpecResponseModel.Response)));

        Assert.DoesNotContain(
            "GetPetResponse",
            Argument(ServiceInterfaceEmitter.GetReturnType(
                raw, EmitterHarness.ModelsNamespace, SpecResponseModel.Response)));
    }

    #endregion
}
