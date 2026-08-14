namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// <c>readOnly</c> and <c>writeOnly</c> through a running pipeline.
/// </summary>
/// <remarks>
/// <para>
/// The generator tests assert on the resolver's text - that a read-only property is not a
/// constructor parameter, that a write-only one has a null getter. That proves what was emitted.
/// These run it.
/// </para>
/// <para>
/// Requests are posted as anonymous objects rather than as <c>Account</c>, so that what arrives is a
/// payload a naive client really could send. Serializing an <c>Account</c> would go out through the
/// same machinery the server reads with, which would prove only that the client behaved.
/// </para>
/// <para>
/// <strong>Enforcement is not reached on this application's serialization path</strong> - see
/// <see cref="TheDirectionsAreNotEnforcedWithoutTheAotResolver"/>, which pins what actually happens
/// today and why.
/// </para>
/// </remarks>
public class ReadOnlyWriteOnlyTests {

    private static async Task<string> BodyText(TestWebResponse response) {
        response.Body.Position = 0;

        using var reader = new StreamReader(response.Body, leaveOpen: true);

        return await reader.ReadToEndAsync();
    }

    /// <summary>
    /// A create request omitting the required read-only property is valid, which is the whole reason
    /// such a property carries no constraints: <c>required</c> here means "always present in a
    /// response", and validating it against the request would reject every correct client.
    /// </summary>
    [HardenedTest]
    public async Task OmittingARequiredReadOnlyPropertyIsNotAValidationError(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(new { email = "someone@example.com" }, "/accounts");

        response.Assert.Ok();
    }

    /// <summary>
    /// A write-only property is accepted, so the keyword does not simply drop the value everywhere.
    /// </summary>
    [HardenedTest]
    public async Task AWriteOnlyValueReachesTheHandler(ITestWebApp testWebApp) {
        await testWebApp.Post(
            new { email = "someone@example.com", password = "correct-horse" }, "/accounts");

        Assert.Equal("correct-horse", AccountServiceImpl.LastPasswordSeen);
    }

    /// <summary>
    /// The boundary, asserted rather than assumed: on this application neither direction is
    /// enforced at run time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both mechanisms live in the generated <c>IJsonTypeInfoResolver</c> - a read-only property is
    /// absent from the constructor metadata, a write-only one has <c>Getter = null</c> - and that
    /// resolver is only consulted when an application registers <c>AotSerializerModule</c>. Without
    /// it, requests and responses go through reflection, which sees an ordinary
    /// <c>{ get; init; }</c> property it may both read and write.
    /// </para>
    /// <para>
    /// Registering <c>AotSerializerModule</c> here does not fix it and is not the missing step: the
    /// generated resolver emits no metadata for collection types, so every list-returning operation
    /// fails with <c>NotSupportedException</c> the moment it is switched on. The resolver has to be
    /// able to serve the application before anything can be enforced through it.
    /// </para>
    /// <para>
    /// This test exists so that the day that changes, it fails and says so - rather than the gap
    /// being rediscovered from a security report. It asserts today's behaviour, and today's
    /// behaviour is wrong.
    /// </para>
    /// </remarks>
    [HardenedTest]
    public async Task TheDirectionsAreNotEnforcedWithoutTheAotResolver(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(
            new { id = "injected-by-client", email = "someone@example.com", password = "hunter2" },
            "/accounts");

        response.Assert.Ok();

        // Should be null: the client must not be able to assign an identifier.
        Assert.Equal("injected-by-client", AccountServiceImpl.LastIdSeen);

        // Should be absent: a write-only value must not come back.
        Assert.Contains("hunter2", await BodyText(response));
    }
}
