using Hardened.Generation.Models;
using Hardened.Smithy.BuildTask.Parsing;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// What a Smithy model's auth traits become.
/// </summary>
/// <remarks>
/// <para>
/// Authentication, and never authorization. Smithy has no equivalent of an OAuth scope, so a model
/// can say a caller must be someone and cannot say what they must hold - which is the whole
/// difference between this front end and the OpenAPI one, and is a property of the language rather
/// than a gap in the reading.
/// </para>
/// <para>
/// These traits were previously <c>Ignorable</c>, on the grounds that "authentication is Hardened's
/// own story rather than the IDL's". That is right about the <em>scheme</em> - which issuer, which
/// token format - and wrong about whether an operation needs one at all, which is a fact about the
/// operation.
/// </para>
/// </remarks>
public class SmithyAuthTests {

    private static string Model(string serviceTraits, string operationTraits) =>
        $$"""
          { "smithy": "2.0", "shapes": {
              "com.example#Svc": {
                "type": "service", "version": "1",
                "operations": [ { "target": "com.example#Op" } ],
                "traits": { {{serviceTraits}} } },
              "com.example#Op": {
                "type": "operation",
                "traits": {
                  "smithy.api#http": { "method": "GET", "uri": "/x", "code": 200 }
                  {{operationTraits}} } } } }
          """;

    private static OperationModel Parse(string serviceTraits, string operationTraits = "") {
        var diagnostics = new List<string>();
        var model = SmithySpecParser.Parse(Model(serviceTraits, operationTraits), "auth", diagnostics);

        Assert.NotNull(model);

        return Assert.Single(Assert.Single(model!.Services).Operations);
    }

    private const string BearerAuth = "\"smithy.api#httpBearerAuth\": {}";

    /// <summary>
    /// A service declaring a scheme requires a caller, and says nothing about what they hold.
    /// </summary>
    [Fact]
    public void AServiceDeclaringASchemeRequiresAuthentication() {
        var branch = Assert.Single(Parse(BearerAuth).AuthorizationBranches);

        Assert.True(branch.RequiresAuthentication);
        Assert.Empty(branch.Grants);
    }

    /// <summary>
    /// A service declaring no scheme requires nothing. There would be nothing to authenticate
    /// against.
    /// </summary>
    [Fact]
    public void AServiceDeclaringNoSchemeRequiresNothing() {
        Assert.Empty(Parse("").AuthorizationBranches);
    }

    /// <summary>
    /// <c>@optionalAuth</c> is how an operation on an authenticated service is made callable without
    /// one.
    /// </summary>
    [Fact]
    public void OptionalAuthOnAnOperationRequiresNothing() {
        Assert.Empty(
            Parse(BearerAuth, ", \"smithy.api#optionalAuth\": {}").AuthorizationBranches);
    }

    /// <summary>
    /// <c>@auth([])</c> narrows the supported schemes to none, which is the other way to say the
    /// same thing.
    /// </summary>
    [Fact]
    public void AnEmptyAuthListOnAnOperationRequiresNothing() {
        Assert.Empty(Parse(BearerAuth, ", \"smithy.api#auth\": []").AuthorizationBranches);
    }

    /// <summary>
    /// A service that narrows to no scheme requires nothing of any operation under it.
    /// </summary>
    [Fact]
    public void AnEmptyAuthListOnTheServiceRequiresNothing() {
        Assert.Empty(
            Parse(BearerAuth + ", \"smithy.api#auth\": []").AuthorizationBranches);
    }

    /// <summary>
    /// The traits are no longer reported as unmodelled, because they are now read.
    /// </summary>
    /// <remarks>
    /// The set is a claim about what the parser does, and a claim nobody tests is how
    /// <c>@uniqueItems</c> came to sit in <c>Mapped</c> with no reader behind it.
    /// </remarks>
    [Fact]
    public void TheAuthTraitsAreNotReportedAsUnmodelled() {
        var diagnostics = new List<string>();

        SmithySpecParser.Parse(
            Model(BearerAuth, ", \"smithy.api#optionalAuth\": {}"), "auth", diagnostics);

        Assert.DoesNotContain(diagnostics, d => d.Contains("httpBearerAuth"));
        Assert.DoesNotContain(diagnostics, d => d.Contains("optionalAuth"));
    }

    /// <summary>
    /// Every scheme the prelude defines is recognised, not only the one the other tests happen to
    /// use.
    /// </summary>
    /// <remarks>
    /// A model picks one of these and never all of them, so a set that recognised four out of five
    /// would look correct in every test written against a service that chose the fifth.
    /// </remarks>
    [Theory]
    [InlineData("smithy.api#httpApiKeyAuth")]
    [InlineData("smithy.api#httpBasicAuth")]
    [InlineData("smithy.api#httpBearerAuth")]
    [InlineData("smithy.api#httpDigestAuth")]
    [InlineData("aws.auth#sigv4")]
    public void EverySchemeTheSetNamesRequiresAuthentication(string scheme) {
        var branch = Assert.Single(Parse($"\"{scheme}\": {{}}").AuthorizationBranches);

        Assert.True(branch.RequiresAuthentication);
    }

    /// <summary>
    /// <c>@auth</c> naming a scheme is a narrowing rather than an opt-out, so it still requires one.
    /// </summary>
    [Fact]
    public void AnAuthListNamingASchemeStillRequiresAuthentication() {
        var branch = Assert.Single(
            Parse(BearerAuth + ", \"smithy.api#auth\": [\"smithy.api#httpBearerAuth\"]")
                .AuthorizationBranches);

        Assert.True(branch.RequiresAuthentication);
    }

    /// <summary>
    /// <c>@auth</c> answers on its own: a service narrowing to no scheme requires nothing even while
    /// it declares one.
    /// </summary>
    [Fact]
    public void AnAuthListTakesPrecedenceOverADeclaredScheme() {
        Assert.Empty(
            Parse(BearerAuth + ", \"smithy.api#auth\": []").AuthorizationBranches);
    }

    /// <summary>
    /// An operation opting out of an authenticated service leaves every sibling guarded.
    /// </summary>
    /// <remarks>
    /// The failure worth catching is an opt-out that leaks: one public operation making the whole
    /// service public would be invisible until somebody called a different route.
    /// </remarks>
    [Fact]
    public void AnOperationOptingOutDoesNotOptOutItsSiblings() {
        var diagnostics = new List<string>();

        var ast = $$"""
                    { "smithy": "2.0", "shapes": {
                        "com.example#Svc": {
                          "type": "service", "version": "1",
                          "operations": [
                            { "target": "com.example#Open" }, { "target": "com.example#Shut" } ],
                          "traits": { {{BearerAuth}} } },
                        "com.example#Open": {
                          "type": "operation",
                          "traits": {
                            "smithy.api#http": { "method": "GET", "uri": "/open", "code": 200 },
                            "smithy.api#optionalAuth": {} } },
                        "com.example#Shut": {
                          "type": "operation",
                          "traits": {
                            "smithy.api#http": { "method": "GET", "uri": "/shut", "code": 200 } } } } }
                    """;

        var model = SmithySpecParser.Parse(ast, "auth", diagnostics);
        var operations = Assert.Single(model!.Services).Operations;

        Assert.Empty(
            Assert.Single(operations, o => o.OperationId == "Open").AuthorizationBranches);

        Assert.Single(
            Assert.Single(operations, o => o.OperationId == "Shut").AuthorizationBranches);
    }
}
