using Hardened.IntegrationTests.OpenApi.SUT.Models;
using Hardened.IntegrationTests.OpenApi.SUT.Services;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Responses;

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

    /// <summary>
    /// <paramref name="tags"/> is the described array parameter, which the generator types as a
    /// <c>List</c> and the binder could not fill: every string-valued source went through one
    /// scalar <c>Parse</c> call, so a repeated key was overwritten before binding and a comma-joined
    /// value failed to convert.
    /// </summary>
    public Task<List<Pet>> ListPets(int? limit, List<string>? tags) {
        var pets = Pets.AsEnumerable();

        if (tags is { Count: > 0 }) {
            pets = pets.Where(pet => pet.Tag != null && tags.Contains(pet.Tag));
        }

        if (limit.HasValue) {
            pets = pets.Take(limit.Value);
        }

        return Task.FromResult(pets.ToList());
    }

    /// <summary>
    /// An array of primitives, which was <c>JsonElement</c> until the element type was carried.
    /// </summary>
    /// <remarks>
    /// Only <c>ResponseArrayItemsRef</c> reached the type mapper, so <c>items: {type: string}</c>
    /// named nothing while array-of-<c>$ref</c> worked - which is exactly why it went unnoticed.
    /// </remarks>
    public Task<List<string>> ListPetNames() =>
        Task.FromResult(Pets.Select(pet => pet.Name).ToList());

    /// <summary>
    /// The 201 the description declares, and the <c>Location</c> it declares beside it.
    /// </summary>
    /// <remarks>
    /// The signature is a response set rather than <c>Task&lt;Pet&gt;</c> because the 201 declares a
    /// header, and a returned <c>Pet</c> has nowhere to put one - <c>Pet</c> is the type
    /// <c>GET /pets/{petId}</c> answers with too, so a header on it would go out on every read.
    /// The description says which header; this says what is in it, which is the half a document
    /// cannot carry.
    /// </remarks>
    public Task<CreatePetResponse> CreatePet(CreatePetRequest body) {
        var created = new Pet("3", body.Name, body.Tag);

        return Task.FromResult<CreatePetResponse>(
            new CreatePetCreated(created, "/pets/" + created.Id));
    }

    /// <summary>
    /// Echoes the enum parameters back on the match, so a test can see what bound.
    /// </summary>
    /// <remarks>
    /// <c>species</c> carries a value that is not a valid C# identifier and <c>size</c> is an
    /// integer enum - the two shapes that could not reach a handler at all before the binder read
    /// the description's vocabulary rather than the member name.
    /// </remarks>
    public Task<List<Pet>> SearchPets(
        string q, string? status, PetSpecies? species, PetSize? size) {
        var matches = Pets
            .Where(pet => pet.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(pet => status == null ? pet : pet with { Status = status })
            .Select(pet => species == null ? pet : pet with { Species = species })
            .Select(pet => size == null ? pet : pet with { Size = size })
            .ToList();

        return Task.FromResult(matches);
    }

    /// <summary>
    /// Returns null for one id, because the document declares a 404 for this operation.
    /// </summary>
    /// <remarks>
    /// The <c>?</c> on the return type is generated from that declaration - it is how the contract
    /// says a null answer is allowed here. The framework turns it into a 404 carrying the
    /// <c>Problem</c> the document declared, with the status and its reason phrase and nothing about
    /// why this handler found nothing.
    /// </remarks>
    public Task<Pet?> GetPet(string petId) {
        // The declared 429, raised. Nothing is generated for it: the description declares a 429
        // with a Problem, and RateLimited<T> is the record the framework already ships for that -
        // so this is the same throw a code-first handler writes, and it carries the Retry-After a
        // generated exception had nowhere to put.
        if (petId == "throttled") {
            throw new RateLimited<Problem>(
                TimeSpan.FromSeconds(30), new Problem { Title = "Slow down." }).AsException();
        }

        return Task.FromResult<Pet?>(
            petId == "missing" ? null : new Pet(petId, "TestPet"));
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

    /// <summary>
    /// Declared as <c>text/plain</c> in the spec, which is what makes the generated handler write
    /// this string straight to the body instead of handing it to the JSON serializer.
    /// </summary>
    public Task<string> PetsAsPlainText() {
        return Task.FromResult(string.Join("\n", Pets.Select(pet => $"{pet.Id}: {pet.Name}")));
    }

    /// <summary>
    /// A scalar <c>text/plain</c> success beside a declared JSON 404, which is the shape whose
    /// errors could not answer at all while the declared set was collected from the successes
    /// alone. The 404 is thrown rather than returned: a scalar success stays non-nullable, so a
    /// throw is the one route to this operation's declared error.
    /// </summary>
    public Task<string> GetPetLabel(string petId, int? copies) {
        if (petId == "missing") {
            throw new NotFound<Problem>(
                new Problem { Status = 404, Title = "Not Found" }).AsException();
        }

        var line = $"Pet {petId}";

        return Task.FromResult(string.Join("\n", Enumerable.Repeat(line, copies ?? 1)));
    }
}
