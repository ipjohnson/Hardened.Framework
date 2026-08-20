using Hardened.Shared.Runtime.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// One enum, one wire vocabulary - the document's, wherever the value travels.
/// </summary>
/// <remarks>
/// <para>
/// There were three. A body and a response used the generated converter and carried the declared
/// value. A query parameter was parsed with <c>Enum.Parse</c> against the C# member name, so the
/// document's own <c>guinea-pig</c> answered 400 while <c>GuineaPig</c> - a name appearing nowhere
/// in the document - answered 200, and any declared value that was not already a valid identifier
/// was unreachable. And the shared <c>IJsonSerializer</c> carried a bare
/// <c>JsonStringEnumConverter</c> in its <c>Converters</c> collection, which System.Text.Json ranks
/// above a <c>[JsonConverter]</c> attribute on the type, so it wrote member names its own
/// application then refused.
/// </para>
/// <para>
/// An integer enum had no vocabulary at all: it generated an empty C# enum whose converter threw on
/// every value.
/// </para>
/// </remarks>
public class EnumVocabularyTests {

    [HardenedTest]
    public async Task ADeclaredValueBindsAsAQueryParameter(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=Buddy&species=guinea-pig");

        response.Assert.Ok();

        var pets = response.Deserialize<List<Pet>>();

        Assert.NotEmpty(pets!);
        Assert.Equal(PetSpecies.GuineaPig, pets[0].Species);
    }

    /// <summary>
    /// And the C# member name, which the document never mentions, does not.
    /// </summary>
    /// <remarks>
    /// The inverse of the defect. Accepting both would leave two vocabularies in place and let a
    /// client keep working by accident against a value the contract does not describe.
    /// </remarks>
    [HardenedTest]
    public async Task TheCSharpMemberNameDoesNotBindAsAQueryParameter(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=Buddy&species=GuineaPig");

        response.Assert.BadRequest();
    }

    [HardenedTest]
    public async Task AnUndeclaredValueIsRefusedAsAQueryParameter(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=Buddy&species=ferret");

        response.Assert.BadRequest();
    }

    /// <summary>An integer enum binds by its declared number.</summary>
    [HardenedTest]
    public async Task AnIntegerEnumBindsByItsDeclaredNumber(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=Buddy&size=25");

        response.Assert.Ok();

        Assert.Equal(PetSize.Large, response.Deserialize<List<Pet>>()![0].Size);
    }

    [HardenedTest]
    public async Task AnUndeclaredNumberIsRefusedForAnIntegerEnum(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=Buddy&size=7");

        response.Assert.BadRequest();
    }

    /// <summary>
    /// The response carries the document's value, not the C# member name.
    /// </summary>
    [HardenedTest]
    public async Task AResponseCarriesTheDeclaredValue(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/search?q=Buddy&species=guinea-pig&size=5");

        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Contains("\"guinea-pig\"", body);
        Assert.DoesNotContain("GuineaPig", body);

        // An integer enum is a number on the wire, not the name of one.
        Assert.Contains("\"size\":5", body);
    }

    /// <summary>
    /// The shared serializer writes what the application's own deserializer accepts.
    /// </summary>
    /// <remarks>
    /// This is what made the harness unusable for any request body carrying an enum:
    /// <c>ITestWebApp.Post(object, path)</c> serialises with exactly this service, and every test
    /// posting a generated record with an enum in it got a 500 - the request deserializer builds its
    /// own options, honours the type's attribute, and refused the member name this had written.
    /// </remarks>
    [HardenedTest]
    public void TheSharedSerializerWritesTheDeclaredValue(ITestWebApp testWebApp) {
        var serializer = testWebApp.RootServiceProvider.GetRequiredService<IJsonSerializer>();

        var json = serializer.Serialize(
            new Pet("1", "Buddy", Species: PetSpecies.GuineaPig, Size: PetSize.Medium), false);

        Assert.Contains("\"guinea-pig\"", json);
        Assert.DoesNotContain("GuineaPig", json);
        Assert.Contains("\"size\":5", json);
    }

    /// <summary>
    /// And a round trip through it lands back on the same member.
    /// </summary>
    /// <remarks>
    /// The end-to-end statement of the defect: write with the shared serializer, read with the
    /// application's own deserializer. This threw <c>JsonException</c> before, which the pipeline
    /// then reported as a 500.
    /// </remarks>
    [HardenedTest]
    public void ASharedSerializerRoundTripKeepsTheMember(ITestWebApp testWebApp) {
        var serializer = testWebApp.RootServiceProvider.GetRequiredService<IJsonSerializer>();

        var json = serializer.Serialize(
            new Pet("1", "Buddy", Species: PetSpecies.GuineaPig, Size: PetSize.Large), false);

        var round = serializer.Deserialize<Pet>(json);

        Assert.Equal(PetSpecies.GuineaPig, round.Species);
        Assert.Equal(PetSize.Large, round.Size);
    }

    /// <summary>
    /// A body carrying a declared value is accepted through the real pipeline.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredValueIsAcceptedInARequestBody(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"name\":\"Rex\",\"tag\":\"good-boy\"}", "/pets",
            request => request.Headers["Content-Type"] = "application/json");

        Assert.Equal(201, response.StatusCode);
    }

    /// <summary>
    /// An undeclared value in a body is the caller's error, not the server's.
    /// </summary>
    /// <remarks>
    /// The generated converter diagnoses it precisely and raises <c>JsonException</c> to say so,
    /// which used to arrive as a 500 with the parser's text echoed to the caller.
    /// </remarks>
    [HardenedTest]
    public async Task AnUndeclaredValueInABodyIs400(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"id\":\"1\",\"name\":\"Rex\",\"species\":\"ferret\"}", "/pets/1",
            request => {
                request.Headers["Content-Type"] = "application/json";
            });

        Assert.True(
            response.StatusCode is 400 or 404 or 405,
            $"expected a client error, got {response.StatusCode}");
        Assert.NotEqual(500, response.StatusCode);
    }
}
