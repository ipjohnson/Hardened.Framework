using Hardened.Generation.Models;
using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The error responses a specification declares, which used to be discarded entirely.
/// </summary>
/// <remarks>
/// The parser took the lowest 2xx and stopped, so a document could describe a 404 and its payload
/// in detail and the generated code contained no trace of either — there was no way to produce the
/// response the document promised.
/// </remarks>
public class DeclaredErrorTests {

    private static OperationModel Operation() {
        var model = OpenApiSpecParser.Parse(Specs.DeclaredErrors, "test", CancellationToken.None);

        Assert.NotNull(model);

        return model!.Services.First(s => s.Tag == "Pet").Operations.Single();
    }

    [Fact]
    public void EveryDeclaredErrorResponseIsParsed() {
        var errors = Operation().ErrorResponses;

        Assert.Equal(new[] { 404, 409, 503 }, errors.Select(e => e.StatusCode).ToArray());
    }

    [Fact]
    public void AnErrorResponseKeepsItsPayloadAndDescription() {
        var notFound = Operation().ErrorResponses.First(e => e.StatusCode == 404);

        Assert.Equal("#/components/schemas/ApiError", notFound.Ref);
        Assert.Equal("No pet with that identifier.", notFound.Description);
    }

    /// <summary>The success response is untouched by any of this.</summary>
    [Fact]
    public void TheSuccessResponseIsUnchanged() {
        var operation = Operation();

        Assert.Equal(200, operation.SuccessStatusCode);
        Assert.Equal("#/components/schemas/Pet", operation.ResponseRef);
    }

    /// <summary>
    /// An error the description did not name binds to the record the framework ships for its
    /// status, and nothing is generated for it.
    /// </summary>
    /// <remarks>
    /// This fixture used to produce three classes - <c>GetPetNotFoundException</c>,
    /// <c>GetPetConflictException</c>, <c>GetPetServiceUnavailableException</c> - the first two
    /// identical but for the status they passed to their base. Nothing downstream read either
    /// type's identity, so the whole of what they were was an integer and a payload type.
    /// </remarks>
    [Fact]
    public void AnUnnamedDeclaredErrorGeneratesNothing() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredErrors).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.DoesNotContain("Exception", generated);
    }

    /// <summary>
    /// And the interface says what to throw instead, because the type no longer says it.
    /// </summary>
    /// <remarks>
    /// The one thing the operation prefix genuinely bought: a reader could learn what
    /// <c>GetPet</c> throws from a type named after it. On the method is where the question belongs
    /// - one line per declared error, rather than a public type per operation and status in the
    /// consumer's assembly.
    /// </remarks>
    [Fact]
    public void TheInterfaceSaysWhatTheOperationThrows() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredErrors).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains(
            "Throws NotFound&lt;ApiError&gt;, Conflict&lt;ApiError&gt;, ServiceUnavailable.",
            generated);
    }

    /// <summary>
    /// An error the author lifted into <c>components/responses</c> keeps that name, and gets one
    /// type however many operations reference it.
    /// </summary>
    /// <remarks>
    /// Both operations in the fixture declare <c>PetMissing</c>. Under the old rule that was
    /// <c>GetPetNotFoundException</c> and <c>GetPetLabelNotFoundException</c> - the same class
    /// under two names, which is the defect this whole change is about.
    /// </remarks>
    [Fact]
    public void ANamedErrorGetsOneTypeForEveryOperationThatDeclaresIt() {
        var generated = OpenApiGenerator.Run(Specs.NamedErrorResponses).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("public partial class PetMissingException", generated);
        Assert.Contains("public partial class PetLockedException", generated);
        Assert.Contains("public partial class DrainingException", generated);

        Assert.DoesNotContain("GetPetLabelNotFoundException", generated);
        Assert.DoesNotContain("GetPetNotFoundException", generated);
    }

    /// <summary>
    /// Two named errors over one schema are two types. The schema names the payload and the
    /// response name names the error, so collapsing them by payload would be the same defect from
    /// the other direction.
    /// </summary>
    [Fact]
    public void TwoNamedErrorsSharingASchemaAreTwoTypes() {
        var generated = OpenApiGenerator.Run(Specs.NamedErrorResponses).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains(
            "public PetMissingException(global::TestNamespace.Models.ApiError value)", generated);
        Assert.Contains(
            "public PetLockedException(global::TestNamespace.Models.ApiError value)", generated);
    }

    /// <summary>
    /// A declared 404 makes the success type nullable, and nothing else about the signature moves.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <c>?</c> is the contract statement: returning null is a declared answer for this
    /// operation, and the framework writes the 404 and the body the document declared for it. An
    /// operation with no declared 404 has no <c>?</c>, and the compiler says so at the return.
    /// </para>
    /// <para>
    /// The assertion here used to be that the signature was unchanged, on the grounds that a
    /// declared error arrives by being thrown. Throwing is still how a handler explains a refusal -
    /// the generated exception type carries a body it wrote. Null is the other half: the answer
    /// when there is nothing to explain, which previously had no way to be stated at all.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeclaredNotFoundMakesTheSuccessTypeNullable() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredErrors).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("Task<global::TestNamespace.Models.Pet?> GetPet(string petId);", generated);
    }

    /// <summary>
    /// A handler throwing the declared error, which is the thing that could not be written.
    /// </summary>
    /// <remarks>
    /// Through the shipped records and <c>AsException()</c>, which is the same throw a code-first
    /// handler writes. That it compiles against types the framework already ships is the point:
    /// the declared error needed no generated type to be answerable.
    /// </remarks>
    [Fact]
    public void AHandlerCanThrowTheDeclaredError() {
        OpenApiGenerator.Run(
                Specs.DeclaredErrors,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task<Pet?> GetPet(string petId) {
                            if (petId == "missing") {
                                throw new NotFound<ApiError>(
                                    new ApiError("not_found", "no such pet")).AsException();
                            }

                            throw new ServiceUnavailable().AsException();
                        }
                    }
                    """))
            .AssertNoErrors();
    }

    /// <summary>
    /// And a handler throwing a named one, which is the type that is still generated.
    /// </summary>
    [Fact]
    public void AHandlerCanThrowANamedError() {
        OpenApiGenerator.Run(
                Specs.NamedErrorResponses,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task<Pet?> GetPet(string petId) {
                            if (petId == "missing") {
                                throw new PetMissingException(new ApiError("not_found", "no such pet"));
                            }

                            throw new DrainingException();
                        }

                        public Task<string> GetPetLabel(string petId) {
                            throw new PetMissingException(new ApiError("not_found", "no such pet"));
                        }
                    }
                    """))
            .AssertNoErrors();
    }

    /// <summary>A response with no declared body takes no payload argument.</summary>
    [Fact]
    public void AnErrorWithNoPayloadTakesNoArgument() {
        var generated = OpenApiGenerator.Run(Specs.NamedErrorResponses).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        var undented = string.Join("\n",
            generated.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()));

        Assert.Contains("public DrainingException()\n: base(503)", undented);

        // The ones that do declare a body get typed access to it.
        Assert.Contains("public PetMissingException(global::TestNamespace.Models.ApiError value)\n: base(404, value)", undented);
        Assert.Contains("public global::TestNamespace.Models.ApiError Body => (global::TestNamespace.Models.ApiError)Value!;", undented);
    }
}
