using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Conformance.CodeFirst.SUT;

/// <summary>
/// The three operations the conformance suite requires, declared with attributes.
/// </summary>
public class PetController {
    private static readonly List<Pet> Pets = [
        new Pet("1", "Buddy"),
        new Pet("2", "Luna", "dog")
    ];

    [Get("/pets")]
    public Task<List<Pet>> ListPets() =>
        Task.FromResult(Pets.ToList());

    /// <summary>Null for an absent pet, which the framework answers as 404.</summary>
    [Get("/pets/{petId}")]
    public Task<Pet?> GetPet(string petId) =>
        Task.FromResult(Pets.FirstOrDefault(pet => pet.Id == petId));

    /// <summary>
    /// 201, declared the way code-first declares it.
    /// </summary>
    /// <remarks>
    /// The described front-ends carry this in the description — <c>code: 201</c> in Smithy, the
    /// response key in OpenAPI. Here it is <c>SuccessStatus</c> on the verb attribute. Three
    /// spellings, one behaviour, which is the whole claim the conformance suite exists to check.
    /// </remarks>
    [Post("/pets", SuccessStatus = 201)]
    public Task<Pet> CreatePet(CreatePetRequest body) =>
        Task.FromResult(new Pet("3", body.Name, body.Tag));

    /// <summary>Requires a caller holding pets:read.</summary>
    [Get("/pets/secured")]
    [AuthorizeGrants("pets:read")]
    public Task<Pet> GetSecuredPet() =>
        Task.FromResult(Pets[0]);
}
