using Hardened.Requests.Runtime.Validation;

namespace Hardened.IntegrationTests.OpenApi.SUT.Tests;

/// <summary>
/// Two silent failures, over a real request: a required member the C# type cannot prove was sent,
/// and a constraint one level below the body.
/// </summary>
/// <remarks>
/// <para>
/// Both used to answer success. A missing required member of a value type became
/// <c>default(T)</c> - an omitted enum became its first declared member, so the API stored a
/// species the caller never named and reported it back as though they had. A constraint on an
/// array's items was emitted, its validator generated and registered in DI, and never called.
/// </para>
/// <para>
/// Bodies are raw JSON rather than typed objects, because what is under test is a member that is
/// <em>absent</em> - and a typed object has no way to leave one out.
/// </para>
/// </remarks>
public class RequiredAndNestedValidationTests {

    private const string Valid =
        """{"species":"cat","weightGrams":3000,"lines":[{"sku":"TLS-0001","quantity":2}]}""";

    private static async Task<RequestValidationError> Rejected(ITestWebApp app, string body) {
        var response = await app.Post(body, "/orders");

        response.Assert.BadRequest();

        var error = response.Deserialize<RequestValidationError>();

        Assert.NotNull(error);
        Assert.Equal("ValidationError", error.Type);
        Assert.NotNull(error.Errors);

        return error;
    }

    /// <summary>
    /// A well-formed order is accepted, so every rejection below fails for the reason it names
    /// rather than because the route never worked.
    /// </summary>
    [HardenedTest]
    public async Task AWellFormedOrderIsAccepted(ITestWebApp testWebApp) {
        var response = await testWebApp.Post(Valid, "/orders");

        response.Assert.Ok();

        Assert.Contains("cat", await response.ReadTextAsync());
    }

    /// <summary>
    /// The one that invented data. Nothing rejects an enum's first declared member, so an omitted
    /// <c>species</c> was stored as <c>dog</c> and answered 200.
    /// </summary>
    [HardenedTest]
    public async Task AMissingRequiredEnumIsRejected(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp, """{"weightGrams":3000,"lines":[{"sku":"TLS-0001"}]}""");

        var field = Assert.Single(error.Errors!, e => e.Field == "body.species");

        Assert.Equal("required", field.Code);
    }

    /// <summary>
    /// The same for an integer with no other constraint. <c>weightGrams</c> is deliberately
    /// unbounded in the contract: a <c>minimum</c> would have caught the absence by accident, which
    /// is how this stayed hidden on fields that happened to have one.
    /// </summary>
    [HardenedTest]
    public async Task AMissingRequiredIntegerIsRejected(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp, """{"species":"cat","lines":[{"sku":"TLS-0001"}]}""");

        var field = Assert.Single(error.Errors!, e => e.Field == "body.weightGrams");

        Assert.Equal("required", field.Code);
    }

    /// <summary>
    /// Both, in one answer. The caller fixes their request in one pass rather than one round trip
    /// per field.
    /// </summary>
    [HardenedTest]
    public async Task EveryMissingRequiredMemberIsNamedAtOnce(ITestWebApp testWebApp) {
        var error = await Rejected(testWebApp, """{"lines":[{"sku":"TLS-0001"}]}""");

        Assert.Contains(error.Errors!, e => e.Field == "body.species");
        Assert.Contains(error.Errors!, e => e.Field == "body.weightGrams");
    }

    /// <summary>
    /// A constraint on an array's items runs. The exact D2 repro: <c>quantity</c> declares
    /// <c>minimum: 1</c> and a zero was accepted and the order placed.
    /// </summary>
    [HardenedTest]
    public async Task AConstraintOnAnArrayItemIsEnforced(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp,
            """{"species":"cat","weightGrams":3000,"lines":[{"sku":"TLS-0001","quantity":0}]}""");

        Assert.Contains(error.Errors!, e => e.Field.Contains("quantity"));
    }

    /// <summary>
    /// The failing element is identified, not merely the array. An error naming <c>lines</c> alone
    /// tells a caller with fifty lines nothing.
    /// </summary>
    [HardenedTest]
    public async Task TheFailingArrayElementIsIdentifiedByItsIndex(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp,
            """
            {"species":"cat","weightGrams":3000,"lines":[
                {"sku":"TLS-0001","quantity":2},
                {"sku":"TLS-0002","quantity":0}]}
            """);

        Assert.Contains(error.Errors!, e => e.Field.Contains("[1]"));
        Assert.DoesNotContain(error.Errors!, e => e.Field.Contains("[0]"));
    }

    /// <summary>
    /// A required member of a nested object is enforced too, not only a range on one.
    /// </summary>
    [HardenedTest]
    public async Task ARequiredMemberOfAnArrayItemIsEnforced(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp,
            """{"species":"cat","weightGrams":3000,"lines":[{"quantity":2}]}""");

        Assert.Contains(error.Errors!, e => e.Field.Contains("sku"));
    }
}
