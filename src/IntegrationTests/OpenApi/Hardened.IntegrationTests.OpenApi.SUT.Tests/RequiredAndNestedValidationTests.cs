using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Responses;

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
    /// A single declared bound composes the single-bound message. The contract says
    /// <c>minimum: 1</c> and nothing else, and the emitted attribute used to fill the absent
    /// maximum with the decimal extreme - so the caller was told the value must be between 1 and
    /// 7.92281625142643E+28, an upper bound nobody declared.
    /// </summary>
    [HardenedTest]
    public async Task ASingleBoundNamesNoInventedExtreme(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp,
            """{"species":"cat","weightGrams":3000,"lines":[{"sku":"TLS-0001","quantity":0}]}""");

        var quantity = Assert.Single(error.Errors!, e => e.Field.Contains("quantity"));

        Assert.DoesNotContain("E+28", quantity.Message);
        Assert.DoesNotContain("between", quantity.Message);
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
    /// A required member of a nested object is enforced too, not only a range on one - and it is
    /// reported under the element that was missing it. An error naming <c>sku</c> against the body
    /// tells a caller with fifty lines nothing, which is the same reason the index is asserted
    /// above.
    /// </summary>
    [HardenedTest]
    public async Task ARequiredMemberOfAnArrayItemIsEnforced(ITestWebApp testWebApp) {
        var error = await Rejected(
            testWebApp,
            """{"species":"cat","weightGrams":3000,"lines":[{"quantity":2}]}""");

        var field = Assert.Single(error.Errors!, e => e.Field == "body.lines[0].sku");

        Assert.Equal("required", field.Code);
        Assert.Equal("sku is required.", field.Message);
    }

    /// <summary>
    /// A literal <c>null</c> body against a <c>requestBody: required: true</c>.
    /// </summary>
    /// <remarks>
    /// The trial's B-01, found spec-first: the generated binder ended in a null-forgiving <c>!</c>,
    /// so the null reached the handler and the first dereference was a 500. The contract says the
    /// body is required, which makes this the one refusal the document had already promised.
    /// </remarks>
    [HardenedTest]
    public async Task ANullBodyIsRefusedRatherThanDereferenced(ITestWebApp testWebApp) {
        var error = await Rejected(testWebApp, "null");

        var field = Assert.Single(error.Errors!);

        Assert.Equal("body", field.Field);
        Assert.Equal("required", field.Code);
    }

    /// <summary>
    /// A missing value type and a missing reference type in one body, answered together.
    /// </summary>
    /// <remarks>
    /// The trial's B-07. Presence was asked in two layers - <c>[JsonRequired]</c> for a value type,
    /// <c>[Required]</c> for a reference type - and the reader aborted before the validator ran, so
    /// a caller was told about <c>weightGrams</c> and learned about <c>lines</c> one round trip
    /// later. The reader aggregates missing members, so asking it once answers both.
    /// </remarks>
    [HardenedTest]
    public async Task AMissingValueMemberDoesNotHideAMissingReferenceMember(ITestWebApp testWebApp) {
        var error = await Rejected(testWebApp, """{"species":"cat"}""");

        Assert.Contains(error.Errors!, e => e.Field == "body.weightGrams");
        Assert.Contains(error.Errors!, e => e.Field == "body.lines");
    }
}
