using Hardened.Web.Runtime.Responses;
namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// An array response is typed by its elements, whether those are a <c>$ref</c> or a primitive.
/// </summary>
/// <remarks>
/// Only the <c>$ref</c> reached the type mapper. <c>items: {type: string}</c> named nothing, so the
/// signature fell through to the untyped response and the operation returned <c>JsonElement</c> -
/// while array-of-<c>$ref</c> worked perfectly, which is exactly what kept it hidden.
/// </remarks>
public class ArrayResponseTests {

    [HardenedTest]
    public async Task AnArrayOfPrimitivesIsTypedByItsElement(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/names");

        response.Assert.Ok();

        var names = response.Deserialize<List<string>>();

        Assert.NotNull(names);
        Assert.Contains("Buddy", names);
    }

    /// <summary>And it is a JSON array of strings on the wire, not a wrapped scalar.</summary>
    [HardenedTest]
    public async Task AnArrayOfPrimitivesSerialisesAsAnArray(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets/names");

        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.StartsWith("[", body);
        Assert.Contains("\"Buddy\"", body);
    }

    /// <summary>The case that always worked, kept beside it so the pair stays honest.</summary>
    [HardenedTest]
    public async Task AnArrayOfRefsIsStillTypedByItsElement(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        response.Assert.Ok();

        Assert.NotEmpty(response.Deserialize<List<Pet>>()!);
    }

    /// <summary>
    /// An optional member the handler left empty is absent from the body rather than null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>tag</c> is optional and not nullable, which says "may be absent, and is a string if
    /// present" - so <c>"tag":null</c> is a body this document forbids. Buddy has no tag and Luna
    /// has one, so both halves are in the same response.
    /// </para>
    /// <para>
    /// The cost of the null was on the client: a generated <c>string tag</c> - which is what
    /// Refitter writes, and what a Swift <c>Codable</c> struct produces - fails to read it. Kiota
    /// makes every property nullable and read it happily, which is why this went unnoticed until a
    /// consumer changed generators.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task AnOptionalMemberWithNoValueIsAbsentRatherThanNull(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/pets");

        response.Assert.Ok();
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.DoesNotContain("null", body);
        Assert.Contains("\"tag\":\"dog\"", body);
    }
}
