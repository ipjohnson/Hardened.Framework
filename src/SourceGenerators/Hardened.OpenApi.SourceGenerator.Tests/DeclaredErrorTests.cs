using Hardened.Idl.Models;
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

    [Fact]
    public void EachDeclaredErrorGetsAnException() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredErrors).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        Assert.Contains("public partial class GetPetNotFoundException", generated);
        Assert.Contains("public partial class GetPetConflictException", generated);
        Assert.Contains("public partial class GetPetServiceUnavailableException", generated);
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
    [Fact]
    public void AHandlerCanThrowTheDeclaredError() {
        OpenApiGenerator.Run(
                Specs.DeclaredErrors,
                OpenApiGenerator.EntryPointWithHandler(
                    """
                    [Handler]
                    public class PetServiceImpl : IPetService {
                        public Task<Pet> GetPet(string petId) {
                            if (petId == "missing") {
                                throw new GetPetNotFoundException(new ApiError("not_found", "no such pet"));
                            }

                            throw new GetPetServiceUnavailableException();
                        }
                    }
                    """))
            .AssertNoErrors();
    }

    /// <summary>A response with no declared body takes no payload argument.</summary>
    [Fact]
    public void AnErrorWithNoPayloadTakesNoArgument() {
        var generated = OpenApiGenerator.Run(Specs.DeclaredErrors).AssertNoErrors()
            .SourceContaining("petstore.g.cs");

        var undented = string.Join("\n",
            generated.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()));

        Assert.Contains("public GetPetServiceUnavailableException()\n: base(503)", undented);

        // The ones that do declare a body get typed access to it.
        Assert.Contains("public GetPetNotFoundException(global::TestNamespace.Models.ApiError value)\n: base(404, value)", undented);
        Assert.Contains("public global::TestNamespace.Models.ApiError Body => (global::TestNamespace.Models.ApiError)Value!;", undented);
    }
}
