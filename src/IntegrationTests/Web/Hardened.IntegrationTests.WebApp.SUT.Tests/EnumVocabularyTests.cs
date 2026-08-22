using System.Text.Json;
using Hardened.IntegrationTests.WebApp.SUT.Controllers;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A code-first enum's wire vocabulary, through the whole application.
/// </summary>
/// <remarks>
/// <para>
/// An enum reaches a client by three routes that share no code - a body written, a body read, and a
/// parameter bound from text - and is described to it by a fourth, the published document. They
/// have disagreed in every combination. Before this, the serializer wrote <c>{"priority":0}</c>
/// while the document declared <c>{"type":"string","enum":["InProgress"]}</c>, so a client
/// generated from the contract could not talk to the application that published it.
/// </para>
/// <para>
/// Held end to end rather than over the emitter, because that is the only level at which the four
/// are the same fact. Each is asserted against a literal, since the literal is what a client sends.
/// </para>
/// </remarks>
public class EnumVocabularyTests {

    /// <summary>
    /// No attribute anywhere: the default an application gets for saying nothing.
    /// </summary>
    [HardenedTest]
    public async Task WriteUsesCamelCaseByDefault(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/enum-vocabulary/ticket");

        Assert.Equal(200, response.StatusCode);

        var body = await response.ReadTextAsync();

        Assert.Contains("\"priority\":\"inProgress\"", body);
        Assert.DoesNotContain("\"priority\":1", body);
    }

    /// <summary>
    /// Reading has to accept what writing produces, or the application refuses its own output.
    /// </summary>
    [HardenedTest]
    public async Task ReadAcceptsTheValueWriteProduces(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new Ticket("Ship it", Priority.OnHold), "/enum-vocabulary/ticket");

        Assert.Equal(200, response.StatusCode);
        Assert.Contains("onHold", await response.ReadTextAsync());
    }

    /// <summary>
    /// A value the application does not declare is refused rather than guessed at.
    /// </summary>
    [HardenedTest]
    public async Task ReadRefusesAnUndeclaredValue(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            "{\"title\":\"x\",\"priority\":\"urgent\"}",
            "/enum-vocabulary/ticket",
            request => request.Headers["Content-Type"] = "application/json");

        Assert.Equal(400, response.StatusCode);
    }

    /// <summary>
    /// The per-enum override, in both directions: one enum opting out of naming entirely and one
    /// choosing a vocabulary that is not a C# identifier at all.
    /// </summary>
    [HardenedTest]
    public async Task ADeclaredNamingOverridesTheDefault(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/enum-vocabulary/order");

        Assert.Equal(200, response.StatusCode);

        var body = await response.ReadTextAsync();

        Assert.Contains("\"code\":\"AB12\"", body);
        Assert.Contains("\"shipping\":\"next-day\"", body);
    }

    /// <summary>
    /// The route that never reaches a JSON converter.
    /// </summary>
    /// <remarks>
    /// A path or query value is text, so the binder converts it rather than the serializer - and
    /// without the same vocabulary registered there, an application accepts a value in a body and
    /// answers 400 to it in a query string. <c>next-day</c> is the case that cannot work by
    /// accident: it is not a valid C# identifier, so nothing reaches it through <c>Enum.Parse</c>.
    /// </remarks>
    [HardenedTest]
    public async Task AQueryParameterBindsTheSameVocabulary(ITestWebApp testWebApp) {
        var byDefault = await testWebApp.Get("/enum-vocabulary/by-priority?priority=inProgress");

        Assert.Equal(200, byDefault.StatusCode);
        Assert.Contains("InProgress", await byDefault.ReadTextAsync());

        var declared = await testWebApp.Get("/enum-vocabulary/by-shipping?shipping=next-day");

        Assert.Equal(200, declared.StatusCode);
        Assert.Contains("NextDay", await declared.ReadTextAsync());
    }

    /// <summary>
    /// The document declares exactly what the serializer writes.
    /// </summary>
    /// <remarks>
    /// The pairing this whole mechanism exists for. The document is the deliverable - consumers
    /// generate clients from it rather than reading the C# - so a vocabulary it does not share with
    /// the wire is a contract that cannot be honoured.
    /// </remarks>
    [HardenedTest]
    public async Task TheDocumentDeclaresTheValuesTheWireCarries(ITestWebApp testWebApp) {
        var response = await testWebApp.Get("/openapi.json");

        Assert.Equal(200, response.StatusCode);

        var schemas = JsonDocument.Parse(await response.ReadTextAsync())
            .RootElement.GetProperty("components").GetProperty("schemas");

        Assert.Equal(
            new[] { "low", "inProgress", "onHold" },
            Values(schemas, "Ticket", "priority"));

        Assert.Equal(new[] { "AB12", "CD34" }, Values(schemas, "Order", "code"));
        Assert.Equal(new[] { "next-day", "two-day" }, Values(schemas, "Order", "shipping"));
    }

    private static string[] Values(JsonElement schemas, string schema, string property) =>
        schemas.GetProperty(schema)
            .GetProperty("properties")
            .GetProperty(property)
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString()!)
            .ToArray();
}
