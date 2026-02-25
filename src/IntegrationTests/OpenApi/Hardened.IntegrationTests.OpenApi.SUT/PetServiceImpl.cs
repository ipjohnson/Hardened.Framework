using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;

namespace Hardened.IntegrationTests.OpenApi.SUT;

/// <summary>
/// This file verifies the OpenAPI source generator produces compilable types.
/// It implements the generated IPetService interface using generated model types.
/// </summary>

[Handler]
public class PetServiceImpl : IPetService {
    public Task<List<Pet>> ListPets(int? limit) {
        var pets = new List<Pet> {
            new Pet("1", "Buddy"),
            new Pet("2", "Luna", "dog")
        };

        if (limit.HasValue) {
            return Task.FromResult(pets.Take(limit.Value).ToList());
        }

        return Task.FromResult(pets);
    }

    public Task<Pet> CreatePet(CreatePetRequest body) {
        return Task.FromResult(new Pet("3", body.Name, body.Tag));
    }

    public Task<Pet> GetPet(string petId) {
        return Task.FromResult(new Pet(petId, "TestPet"));
    }

    public Task DeletePet(string petId) {
        return Task.CompletedTask;
    }
}
