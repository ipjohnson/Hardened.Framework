using Hardened.IntegrationTests.Smithy.SUT.Models;
using Hardened.IntegrationTests.Smithy.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.Smithy.SUT;

/// <summary>
/// Implements the interface generated from a Smithy model.
/// </summary>
/// <remarks>
/// <para>
/// This file is the proof. Every type it names - <c>IPetStoreService</c>, <c>Pet</c>,
/// <c>PetKind</c>, the three input and output records - was emitted from a JSON AST by emitters
/// that have never heard of Smithy, and it has to compile against them. A reader that produced a
/// model the spine could not emit, or emitted something that would not bind, fails here rather than
/// in an assertion about a string.
/// </para>
/// <para>
/// Every method is deliberately trivial, for the same reason the OpenAPI fixture's are: what is
/// under test is the generated routing, binding and validation around them.
/// </para>
/// </remarks>
[Handler]
public class PetStoreServiceImpl : IPetStoreService {

    private static readonly List<Pet> Pets = [
        new Pet("1", "Buddy", PetKind.Dog),
        new Pet("2", "Luna", PetKind.Cat, "Lu")
    ];

    /// <summary>
    /// Takes the whole input structure as the body, which is what an operation whose members carry
    /// no binding trait means.
    /// </summary>
    public Task<CreatePetOutput> CreatePet(CreatePetInput body) {
        var created = new Pet("3", body.Name, body.Kind);

        // location carries @httpHeader("Location"), so it is a member of the output like any other
        // and the handler sets it - it just leaves as a header rather than in the JSON. The
        // signature is unchanged by the binding, because the type that carries the header is the
        // type that was already being returned.
        return Task.FromResult(new CreatePetOutput(created, "/pets/" + created.Id));
    }

    /// <summary>
    /// The three parameters are the three bindings: <c>@httpLabel</c>, <c>@httpQuery("verbose")</c>
    /// and <c>@httpHeader("X-Trace-Id")</c>. The header's C# name comes from its wire name, because
    /// NameAllocator assigns every name in the model from the wire spelling.
    /// </summary>
    /// <remarks>
    /// The return is nullable because the model declares a <c>PetNotFound</c> error for this
    /// operation, which is the contract saying a null answer is allowed here. Returning null
    /// answers 404; throwing <c>PetNotFoundException</c> is how a handler says more than that.
    /// </remarks>
    public Task<GetPetOutput?> GetPet(string petId, bool? verbose, string? xTraceId) {
        // The declared Throttled error, raised. The exception is named for the error shape the
        // model declares, which is the name every other Smithy code generator gives it - and one
        // type, however many operations bind the shape. AsException() infers it from the body, so
        // the shape is named once.
        if (petId == "throttled") {
            throw new Throttled("Slow down.").AsException();
        }

        var pet = Pets.FirstOrDefault(p => p.Id == petId);

        if (pet == null) {
            return Task.FromResult<GetPetOutput?>(null);
        }

        return Task.FromResult<GetPetOutput?>(new GetPetOutput(
            verbose == true ? pet : pet with { Nickname = null }));
    }

    public Task<ListPetsOutput> ListPets(int? limit, PetKind? kind) =>
        Task.FromResult(new ListPetsOutput(
            limit.HasValue ? Pets.Take(limit.Value).ToList() : Pets.ToList()));

    /// <summary>
    /// Reached only by an authenticated caller — the service declares @httpBearerAuth and this
    /// operation does not opt out with @auth([]).
    /// </summary>
    public Task<GetSecuredPetOutput> GetSecuredPet() =>
        Task.FromResult(new GetSecuredPetOutput(Pets[0]));
}
