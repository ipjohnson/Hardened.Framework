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
/// <b>A declared error is usually not a case type at all.</b> It resolves to the record the
/// framework already ships for its status, so the operation's container names
/// <c>NotFound&lt;ApiError&gt;</c> and nothing is emitted for it. What still gets a case type -
/// a named error, one carrying a header, a status registered nowhere - is
/// <see cref="ShippedResponsesTests"/>' subject, and the shape of one is under
/// <c>EmitErrorCaseTypes</c> below.
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

    /// <summary>An error the shipped set cannot express, with the name the allocator would give it.</summary>
    private static ErrorResponseModel NamedError(
        int status, string? schemaRef, string name, string typeName) =>
        new() { StatusCode = status, Ref = schemaRef, Name = name, TypeName = typeName };

    private static string EmitCases(params ErrorResponseModel[] errors) =>
        EmitterHarness.Write(ns => UnionResponseEmitter.EmitErrorCaseTypes(
            ns, new List<ErrorResponseModel>(errors), EmitterHarness.ModelsNamespace));

    /// <summary>The shipped record for a status, spelled as the container names it.</summary>
    private static string Shipped(string name) =>
        "Hardened.Requests.Abstract.Responses." + name;

    private static string Emit(params OperationModel[] operations) =>
        Emit(asLanguageUnion: false, operations);

    private static string Emit(bool asLanguageUnion, params OperationModel[] operations) =>
        EmitterHarness.Write(ns => UnionResponseEmitter.Emit(
            ns,
            new ServiceModel { Tag = "pets", Operations = new List<OperationModel>(operations) },
            EmitterHarness.ModelsNamespace,
            asLanguageUnion));

    /// <summary>The same, with the schemas the errors' bodies resolve against, which is what the shorthand needs.</summary>
    private static string EmitWithSchemas(IReadOnlyList<SchemaModel> schemas, params OperationModel[] operations) =>
        EmitWithSchemas(schemas, asLanguageUnion: false, operations);

    private static string EmitWithSchemas(
        IReadOnlyList<SchemaModel> schemas, bool asLanguageUnion, params OperationModel[] operations) =>
        EmitterHarness.Write(ns => UnionResponseEmitter.Emit(
            ns,
            new ServiceModel { Tag = "pets", Operations = new List<OperationModel>(operations) },
            EmitterHarness.ModelsNamespace,
            asLanguageUnion,
            schemas: schemas,
            specFileName: "petstore"));

    private static SchemaModel ProblemSchema(string name) {
        var schema = new SchemaModel { Name = name, Kind = SchemaKind.Object };

        schema.Properties.Add(new PropertyModel { Name = "title", Type = "string" });
        schema.Properties.Add(new PropertyModel { Name = "status", Type = "integer" });
        schema.Properties.Add(new PropertyModel { Name = "detail", Type = "string" });

        return schema;
    }

    #region the case types

    /// <summary>
    /// A declared error resolves to the record the framework ships for its status, so no case type
    /// is emitted for it. This is where <c>GetPetNotFound</c> and <c>GetPetConflict</c> used to be.
    /// </summary>
    [Fact]
    public void ADeclaredErrorResolvesToTheShippedRecord() {
        var emitted = Emit(Operation(
            errors: [Error(404, "#/components/schemas/ApiError"), Error(409, "#/components/schemas/ApiError")]));

        Assert.DoesNotContain("record GetPetNotFound", emitted);
        Assert.DoesNotContain("record GetPetConflict", emitted);
    }

    /// <summary>
    /// The reason the wrappers exist at all. The repo's own fixture declares 404 and 409 both
    /// referencing ApiError, so the unwrapped shape would be two identical conversions - CS0457 at
    /// the point of use.
    /// </summary>
    /// <remarks>
    /// Still two distinct types, and that was always what the constraint asked for. The per-status
    /// wrapper is what clears CS0457; the operation prefix never was.
    /// </remarks>
    [Fact]
    public void TwoStatusesSharingASchemaBecomeTwoDistinctCaseTypes() {
        var emitted = Emit(Operation(
            errors: [Error(404, "#/components/schemas/ApiError"), Error(409, "#/components/schemas/ApiError")]));

        Assert.Contains(
            $"GetPetResponse({Shipped("NotFound")}<Test.Api.Models.ApiError> value)", emitted);
        Assert.Contains(
            $"GetPetResponse({Shipped("Conflict")}<Test.Api.Models.ApiError> value)", emitted);
    }

    /// <summary>
    /// A status declaring no body resolves to the bare shipped form, which carries the framework's
    /// own problem document rather than a schema the description never named.
    /// </summary>
    [Fact]
    public void AStatusWithNoBodyResolvesToTheBareShippedForm() {
        var emitted = Emit(Operation(errors: [Error(503, null)]));

        Assert.Contains($"GetPetResponse({Shipped("ServiceUnavailable")} value)", emitted);
        Assert.DoesNotContain("GetPetServiceUnavailable", emitted);
    }

    /// <summary>
    /// A registered status with no shipped record closes the one generic over a marker, so the
    /// framework costs a line per status instead of a record.
    /// </summary>
    [Fact]
    public void AStatusWithNoShippedRecordResolvesToAClosedStatusGeneric() {
        var emitted = Emit(Operation(errors: [Error(418, "#/components/schemas/ApiError")]));

        Assert.Contains(
            $"GetPetResponse({Shipped("Status")}<{Shipped("Http.ImATeapot")},Test.Api.Models.ApiError> value)",
            emitted);
    }

    /// <summary>
    /// And a status registered nowhere is the one case a table cannot answer, so it gets a type.
    /// </summary>
    [Fact]
    public void AnUnregisteredStatusStillGetsACaseType() {
        var emitted = Emit(Operation(
            errors: [new ErrorResponseModel {
                StatusCode = 529, Ref = "#/components/schemas/ApiError", TypeName = "Status529ApiError"
            }]));

        Assert.Contains("GetPetResponse(Test.Api.Models.Status529ApiError value)", emitted);
    }

    /// <summary>
    /// Each generated case carries its status as <c>[HttpStatus]</c>, which is how the dispatch
    /// generator resolves it - the specification is not there to read by then. It is the same
    /// attribute a hand-written response type carries, so one status resolution serves both front
    /// ends.
    /// </summary>
    [Fact]
    public void EachCaseTypeCarriesItsStatusAsAnAttribute() {
        Assert.Contains(
            "HttpStatus(404)",
            EmitCases(NamedError(404, "#/components/schemas/ApiError", "PetMissing", "PetMissingError")));
    }

    /// <summary>
    /// Sealed, because a case type assignable to another case in the same set has no unambiguous
    /// match order. Partial, because that is a different question.
    /// </summary>
    /// <remarks>
    /// Sealing forbids deriving; it never needed to forbid extending in place. A generated type an
    /// application cannot add an interface or a computed member to is one it has to wrap instead,
    /// and the two modifiers together give the match-order guarantee without that cost.
    /// </remarks>
    [Fact]
    public void CaseTypesAreSealedPartialRecords() {
        Assert.Contains(
            "public sealed partial record PetMissingError",
            EmitCases(NamedError(404, "#/components/schemas/ApiError", "PetMissing", "PetMissingError")));
    }

    /// <summary>
    /// One case type per distinct error, not one per operation that declares it. Two operations
    /// declaring the same 404 used to emit the same record twice under two names.
    /// </summary>
    [Fact]
    public void OneCaseTypePerDistinctError() {
        var emitted = EmitCases(
            NamedError(404, "#/components/schemas/ApiError", "PetMissing", "PetMissingError"),
            NamedError(409, "#/components/schemas/ApiError", "PetTaken", "PetTakenError"));

        Assert.Contains("public sealed partial record PetMissingError", emitted);
        Assert.Contains("public sealed partial record PetTakenError", emitted);
    }

    /// <summary>
    /// The name comes off the model rather than from the operation and the status. Nothing here
    /// re-derives it: the allocator arbitrated it against the schema names, which is the one place
    /// that can be decided.
    /// </summary>
    [Fact]
    public void ACaseTypeTakesTheNameOnTheModel() {
        Assert.Contains(
            "public sealed partial record AccountNotFoundError",
            EmitCases(NamedError(
                400, "#/components/schemas/AccountNotFound", "AccountNotFound",
                "AccountNotFoundError")));
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
        Assert.Contains(
            $"public GetPetResponse({Shipped("NotFound")}<Test.Api.Models.ApiError> value)", emitted);
    }

    /// <summary>
    /// One conversion per case, so a handler returns the bare value rather than wrapping it.
    /// </summary>
    [Fact]
    public void TheContainerConvertsFromEveryCase() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("implicit operator GetPetResponse(Test.Api.Models.Pet value)", emitted);
        Assert.Contains(
            $"implicit operator GetPetResponse({Shipped("NotFound")}<Test.Api.Models.ApiError> value)",
            emitted);
    }

    /// <summary>
    /// The shorthand: the bare shipped record converts too, through the holder that fills the
    /// contract's body from it, so a handler returns <c>new NotFound("pet", "...")</c>.
    /// </summary>
    [Fact]
    public void TheContainerConvertsFromTheBareRecordWhereTheBodyCanBeFilled() {
        var emitted = EmitWithSchemas(
            [ProblemSchema("ApiError")], Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains(
            $"public static implicit operator GetPetResponse({Shipped("NotFound")} value) => " +
            "new(global::Test.Api.Models.PetstoreProblems.NotFoundApiError(value));",
            emitted);
    }

    /// <summary>
    /// A body the record's facts have no members in gets no shorthand, so the wrong shape is a
    /// compile error rather than a body with nothing in it.
    /// </summary>
    [Fact]
    public void ABodyThatIsNotProblemShapedGetsNoShorthand() {
        var plain = new SchemaModel { Name = "ApiError", Kind = SchemaKind.Object };
        plain.Properties.Add(new PropertyModel { Name = "message", Type = "string" });

        var emitted = EmitWithSchemas([plain], Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.DoesNotContain($"operator GetPetResponse({Shipped("NotFound")} value)", emitted);
    }

    /// <summary>Without the schemas there is no way to tell, and nothing is written.</summary>
    [Fact]
    public void WithoutSchemasNoShorthandIsWritten() {
        var emitted = Emit(Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.DoesNotContain("PetstoreProblems", emitted);
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
        Assert.Contains($"public GetPetResponse({Shipped("NotFound")} value)", emitted);
    }

    #endregion

    #region the language union

    /// <summary>
    /// Union mode declares the container with the keyword. Every member the struct spells out - the
    /// constructors, the conversions, Value - the compiler synthesises from the case list, which is
    /// why the declaration is the whole of it where there is no shorthand to write.
    /// </summary>
    [Fact]
    public void UnionModeDeclaresTheContainerWithTheKeyword() {
        var emitted = Emit(
            asLanguageUnion: true,
            Operation(errors: [Error(404, "#/components/schemas/ApiError"), Error(503, null)]));

        Assert.Contains(
            "public union GetPetResponse(Pet, NotFound<ApiError>, ServiceUnavailable);", emitted);

        Assert.DoesNotContain("public struct GetPetResponse", emitted);
        Assert.DoesNotContain("implicit operator GetPetResponse", emitted);
    }

    /// <summary>
    /// The shorthand from the bare record is written in union mode too, as the one kind of member
    /// in a body the declaration otherwise does without - so a handler returns
    /// <c>new NotFound("pet", "...")</c> whichever container the module chose.
    /// </summary>
    [Fact]
    public void UnionModeWritesTheShorthandInABody() {
        var emitted = EmitWithSchemas(
            [ProblemSchema("ApiError")], asLanguageUnion: true,
            Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("public union GetPetResponse(Pet, NotFound<ApiError>)", emitted);
        Assert.DoesNotContain("public union GetPetResponse(Pet, NotFound<ApiError>);", emitted);
        Assert.Contains(
            $"public static implicit operator GetPetResponse({Shipped("NotFound")} value) => " +
            "new(global::Test.Api.Models.PetstoreProblems.NotFoundApiError(value));",
            emitted);
    }

    /// <summary>A body the shorthand cannot fill leaves the union on one line.</summary>
    [Fact]
    public void UnionModeStaysOnOneLineWithoutAShorthandToWrite() {
        var plain = new SchemaModel { Name = "ApiError", Kind = SchemaKind.Object };
        plain.Properties.Add(new PropertyModel { Name = "message", Type = "string" });

        var emitted = EmitWithSchemas(
            [plain], asLanguageUnion: true, Operation(errors: [Error(404, "#/components/schemas/ApiError")]));

        Assert.Contains("public union GetPetResponse(Pet, NotFound<ApiError>);", emitted);
        Assert.DoesNotContain("implicit operator GetPetResponse", emitted);
    }

    /// <summary>
    /// The case types are emitted byte-identical in both modes, which is what makes moving between
    /// them a container swap rather than a migration.
    /// </summary>
    [Fact]
    public void TheCaseTypesAreIdenticalInBothModes() {
        var operation = Operation("CancelPet", responseRef: null);

        operation.SuccessResponses.Add(new SuccessResponseModel { StatusCode = 204 });
        operation.SuccessResponses.Add(new SuccessResponseModel { StatusCode = 202 });

        var asStruct = Emit(asLanguageUnion: false, operation);
        var asUnion = Emit(asLanguageUnion: true, operation);

        foreach (var line in new[] {
                     "public sealed partial record CancelPetNoContent",
                     "HttpStatus(204)",
                     "public sealed partial record CancelPetAccepted",
                     "HttpStatus(202)"
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
            operation, EmitterHarness.ModelsNamespace, SpecResponseModel.Throws);

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
