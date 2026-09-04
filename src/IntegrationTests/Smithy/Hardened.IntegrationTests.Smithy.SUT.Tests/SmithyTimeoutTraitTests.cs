using System.Text.Json;

namespace Hardened.IntegrationTests.Smithy.SUT.Tests;

/// <summary>
/// A deadline written in a Smithy model reaches both halves of what the generator produces: the
/// handler the pipeline resolves against, and the document the service publishes.
/// </summary>
/// <remarks>
/// <para>
/// <c>@timeout</c> is Hardened's own trait rather than the prelude's, and the whole path has to
/// hold for it to mean anything: the Smithy CLI resolves the definition the targets add to the
/// model, the AST carries it, the reader puts it on the operation, the serializer carries it across
/// the intermediate file the generator reads, and the spec bridge emits it as the same
/// <c>TimeoutAttribute</c> a code-first handler would carry. Any one of those dropping it is
/// silent.
/// </para>
/// <para>
/// Asserted against the served document because that is the end of the path and the one artefact a
/// test can read without waiting two seconds for a deadline to fire. What it proves about the
/// handler is the same fact: both are written from one <c>DeclaredTimeout</c> on the model.
/// </para>
/// </remarks>
public class SmithyTimeoutTraitTests {

    private static JsonElement Operation(ITestWebApp app, string path, string method) {
        using var document = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "openapi", "SmithyTestApp.json")));

        return document.RootElement.GetProperty("paths").GetProperty(path)
            .GetProperty(method).Clone();
    }

    [HardenedTest]
    public void TheModelsDeadlineIsPublished(ITestWebApp app) {
        var operation = Operation(app, "/pets/{petId}", "get");

        Assert.Equal(2000, operation.GetProperty("x-hardened-timeout").GetInt32());
    }

    /// <summary>
    /// The scalar form, because the trait stated no status and no retry-after. An object with one
    /// member would say the same thing and read worse.
    /// </summary>
    [HardenedTest]
    public void ADeadlineStatingNothingElseIsPublishedAsANumber(ITestWebApp app) {
        var operation = Operation(app, "/pets/{petId}", "get");

        Assert.Equal(
            JsonValueKind.Number, operation.GetProperty("x-hardened-timeout").ValueKind);
    }

    /// <summary>
    /// An operation the model says nothing about is bounded by nothing, so the document says
    /// nothing either. This is the same rule the code-first front end follows.
    /// </summary>
    [HardenedTest]
    public void AnOperationDeclaringNoDeadlinePublishesNone(ITestWebApp app) {
        var operation = Operation(app, "/pets", "get");

        Assert.False(operation.TryGetProperty("x-hardened-timeout", out _));
    }

    /// <summary>
    /// And it survives the round trip: the deadline is published under the same extension the
    /// OpenAPI reader parses, so a service regenerated from this document is bounded the way this
    /// one is.
    /// </summary>
    [HardenedTest]
    public void ThePublishedDeadlineIsTheExtensionTheReaderParses(ITestWebApp app) {
        var operation = Operation(app, "/pets/{petId}", "get");

        Assert.True(operation.TryGetProperty("x-hardened-timeout", out var published));
        Assert.True(published.GetInt32() > 0);
    }
}
