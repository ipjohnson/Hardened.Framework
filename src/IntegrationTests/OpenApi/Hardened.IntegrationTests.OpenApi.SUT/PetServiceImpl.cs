using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// This file verifies the OpenAPI source generator produces compilable types.
/// It implements the generated IPetService interface using generated model types.
///
/// <para>
/// Every method here is deliberately trivial. The point of the integration suite is what the
/// generated routing, binding and validation do around it, so a method that did real work would
/// only make a failure harder to attribute.
/// </para>
/// </summary>
[Handler]
public class PetServiceImpl : IPetService {
    private static readonly List<Pet> Pets = [
        new Pet("1", "Buddy"),
        new Pet("2", "Luna", "dog")
    ];

    public Task<List<Pet>> ListPets(int? limit) {
        if (limit.HasValue) {
            return Task.FromResult(Pets.Take(limit.Value).ToList());
        }

        return Task.FromResult(Pets.ToList());
    }

    public Task<Pet> CreatePet(CreatePetRequest body) {
        return Task.FromResult(new Pet("3", body.Name, body.Tag));
    }

    public Task<List<Pet>> SearchPets(string q, string? status) {
        var matches = Pets
            .Where(pet => pet.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(pet => status == null ? pet : pet with { Status = status })
            .ToList();

        return Task.FromResult(matches);
    }

    public Task<Pet> GetPet(string petId) {
        return Task.FromResult(new Pet(petId, "TestPet"));
    }

    public Task<Pet> ReplacePet(string petId, CreatePetRequest body) {
        return Task.FromResult(new Pet(petId, body.Name, body.Tag));
    }

    public Task<Pet> UpdatePet(string petId, UpdatePetRequest body) {
        return Task.FromResult(new Pet(petId, body.Name ?? "TestPet", body.Rating?.ToString()));
    }

    public Task DeletePet(string petId) {
        return Task.CompletedTask;
    }
}
