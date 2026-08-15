namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

public class PetControllerTests {
    [HardenedTest]
    public async Task ListPets_ReturnsListOfPets(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        response.Assert.Ok();

        var pets = response.Deserialize<List<Pet>>();
        Assert.NotNull(pets);
        Assert.Equal(2, pets.Count);
        Assert.Contains(pets, p => p.Name == "Buddy");
        Assert.Contains(pets, p => p.Name == "Luna");
    }

    [HardenedTest]
    public async Task ListPets_WithQueryParameter_ReturnsOk(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?limit=1");

        response.Assert.Ok();

        var pets = response.Deserialize<List<Pet>>();
        Assert.NotNull(pets);
        Assert.NotEmpty(pets);
    }

    [HardenedTest]
    public async Task CreatePet_WithBody_ReturnsPet(ITestWebApp testWebApp) {
        var request = new CreatePetRequest("Whiskers", "cat");
        var response = await testWebApp.Post(request, "/pets");

        response.Assert.Ok();

        var pet = response.Deserialize<Pet>();
        Assert.NotNull(pet);
        Assert.Equal("Whiskers", pet.Name);
        Assert.Equal("cat", pet.Tag);
    }

    [HardenedTest]
    public async Task GetPet_WithPathParameter_ReturnsPet(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/42");

        response.Assert.Ok();

        var pet = response.Deserialize<Pet>();
        Assert.NotNull(pet);
        Assert.Equal("42", pet.Id);
        Assert.Equal("TestPet", pet.Name);
    }

    [HardenedTest]
    public async Task DeletePet_ReturnsOk(ITestWebApp testWebApp) {
        var response = await testWebApp.Delete("/pets/42");

        response.Assert.Ok();
    }

    /// <summary>
    /// A path template describes one segment, and the table compiled from it matches one.
    ///
    /// <para>
    /// Until 2026-08-15 <c>/pets/{petId}</c> answered any deeper path, binding
    /// <c>petId = "42/anything/at/all"</c> — so a document declaring four operations served an
    /// unbounded number of paths, and a client could not tell a real route from a typo. There is no
    /// catch-all on this side to weigh against it: an OpenAPI template expression is a parameter
    /// name, and cannot ask for the rest of the path.
    /// </para>
    /// </summary>
    [HardenedTest]
    public async Task GetPet_WithADeeperPath_IsNotFound(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/42/anything/at/all");

        response.Assert.NotFound();
    }
}
