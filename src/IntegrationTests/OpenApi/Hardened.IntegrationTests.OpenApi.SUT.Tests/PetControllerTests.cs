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

    #region a declared error header

    /// <summary>
    /// A 429 declared once in <c>components.responses</c> with a <c>Retry-After</c> and referenced
    /// by two operations. Both still return their plain types: an error's headers used to count
    /// towards "this operation needs a response set", so a header added to one shared entry changed
    /// the signature of every operation that named it.
    /// </summary>
    /// <remarks>
    /// The signatures are asserted by this project compiling at all - <c>PetServiceImpl</c>
    /// implements the generated interface, and a widened one would not match.
    /// </remarks>
    [HardenedTest]
    public async Task ListPets_WhenThrottled_AnswersTheDeclaredStatus(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?tags=throttled");

        Assert.Equal(429, response.StatusCode);
    }

    /// <summary>
    /// And the header the description declares is on it. Throws mode reaches the error by
    /// throwing, so the generated exception is what has to carry it - which is what the widening
    /// was standing in for, at the cost of every signature that mentioned the error.
    /// </summary>
    [HardenedTest]
    public async Task ListPets_WhenThrottled_SendsTheDeclaredHeader(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?tags=throttled");

        Assert.Equal("30", response.Headers["Retry-After"].ToString());
    }

    #endregion

    #region the described array parameter

    /// <summary>
    /// <c>tags</c> is <c>type: array</c> in the description, so the generator types it as a
    /// <c>List</c>. Nothing could fill one: the query parser overwrote a repeated key, and the
    /// binder handed whatever survived to a scalar <c>Parse</c> that threw for a list.
    /// </summary>
    [HardenedTest]
    public async Task ListPets_WithARepeatedArrayParameter_FiltersByEveryValue(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?tags=dog&tags=cat");

        response.Assert.Ok();

        var pets = response.Deserialize<List<Pet>>();

        Assert.NotNull(pets);
        Assert.Equal("Luna", Assert.Single(pets).Name);
    }

    /// <summary>The same parameter written as <c>explode: false</c>.</summary>
    [HardenedTest]
    public async Task ListPets_WithACommaJoinedArrayParameter_FiltersByEveryValue(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?tags=dog,cat");

        response.Assert.Ok();

        var pets = response.Deserialize<List<Pet>>();

        Assert.NotNull(pets);
        Assert.Equal("Luna", Assert.Single(pets).Name);
    }

    [HardenedTest]
    public async Task ListPets_WithNoArrayParameter_FiltersNothing(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        response.Assert.Ok();

        Assert.Equal(2, response.Deserialize<List<Pet>>()!.Count);
    }

    /// <summary>Both parameters on one operation, neither disturbing the other's binding.</summary>
    [HardenedTest]
    public async Task ListPets_WithBothParameters_BindsBoth(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?tags=dog&tags=cat&limit=1");

        response.Assert.Ok();

        Assert.Equal("Luna", Assert.Single(response.Deserialize<List<Pet>>()!).Name);
    }

    /// <summary>
    /// And the constraint on the scalar one still fires beside the array one. The array parameter
    /// is compiled into the same validator, so a change to it could have taken the other's bounds
    /// with it.
    /// </summary>
    [HardenedTest]
    public async Task ListPets_WithAnArrayParameterAndAnOutOfRangeLimit_IsRefused(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets?tags=dog&limit=500");

        response.Assert.BadRequest();
    }

    #endregion

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
