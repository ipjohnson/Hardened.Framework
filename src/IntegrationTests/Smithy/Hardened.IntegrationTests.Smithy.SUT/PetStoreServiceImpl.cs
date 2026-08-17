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
    public Task<CreatePetOutput> CreatePet(CreatePetInput body) =>
        Task.FromResult(new CreatePetOutput(new Pet("3", body.Name, body.Kind)));

    /// <summary>
    /// The three parameters are the three bindings: <c>@httpLabel</c>, <c>@httpQuery("verbose")</c>
    /// and <c>@httpHeader("X-Trace-Id")</c>. The header's C# name comes from its wire name, because
    /// NameAllocator assigns every name in the model from the wire spelling.
    /// </summary>
    public Task<GetPetOutput> GetPet(string petId, bool? verbose, string? xTraceId) {
        var pet = Pets.FirstOrDefault(p => p.Id == petId) ?? Pets[0];

        return Task.FromResult(new GetPetOutput(
            verbose == true ? pet : pet with { Nickname = null }));
    }

    public Task<ListPetsOutput> ListPets(int? limit) =>
        Task.FromResult(new ListPetsOutput(
            limit.HasValue ? Pets.Take(limit.Value).ToList() : Pets.ToList()));
}
