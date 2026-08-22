using System.Threading;
using Hardened.Idl;
using Hardened.Idl.Models;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// What a description's <c>security</c> becomes.
/// </summary>
/// <remarks>
/// <para>
/// Scopes, not schemes. A scheme says how a caller proves who they are, which is configuration this
/// application already owns and a description cannot know. Scopes say which permissions an operation
/// needs, which is a fact about the operation and maps onto <c>Requirement</c> directly.
/// </para>
/// <para>
/// Every rule here has a failure mode that produces a <em>weaker</em> API than the document
/// describes, which is why they are pinned individually rather than through one round-trip.
/// </para>
/// </remarks>
public class DescribedAuthorizationTests {

    private const string Schemes =
        """
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                clientCredentials:
                  tokenUrl: https://example.invalid/token
                  scopes:
                    "pets:read": Read.
                    "pets:write": Write.
            key:
              type: apiKey
              name: X-Api-Key
              in: header
          schemas:
            Pet:
              type: object
              properties:
                id: { type: string }
        """;

    private static OperationModel Operation(
        string operationSecurity, string documentSecurity = "",
        ICollection<string>? diagnostics = null) =>
        Assert.Single(
            Assert.Single(
                OpenApiSpecParser.Parse(
                    $$"""
                     openapi: "3.0.0"
                     info: { title: Pets, version: "1.0" }
                     {{documentSecurity}}
                     paths:
                       /pets:
                         get:
                           tags: [Pet]
                           operationId: listPets
                     {{operationSecurity}}
                           responses:
                             '200':
                               description: A pet
                               content:
                                 application/json:
                                   schema:
                                     $ref: '#/components/schemas/Pet'
                     {{Schemes}}
                     """,
                    "test",
                    CancellationToken.None,
                    diagnostics: diagnostics)!.Services).Operations);

    private static AuthorizationBranchModel Only(OperationModel operation) =>
        Assert.Single(operation.AuthorizationBranches);

    /// <summary>
    /// The common shape: one scheme, one scope. Becomes one grant.
    /// </summary>
    [Fact]
    public void AScopeBecomesAGrant() {
        var branch = Only(Operation("""      security: [{ oauth: ["pets:read"] }]"""));

        Assert.Equal(new[] { "pets:read" }, branch.Grants);
        Assert.False(branch.RequiresAuthentication);
    }

    /// <summary>
    /// Several scopes on one scheme are conjoined - the token needs all of them, not any.
    /// </summary>
    [Fact]
    public void SeveralScopesOnOneSchemeAreAllRequired() {
        var branch = Only(Operation("""      security: [{ oauth: ["pets:read", "pets:write"] }]"""));

        Assert.Equal(new[] { "pets:read", "pets:write" }, branch.Grants);
    }

    /// <summary>
    /// A scheme that cannot carry scopes says "be authenticated" - which is a requirement, not the
    /// absence of one.
    /// </summary>
    /// <remarks>
    /// The load-bearing one. Reading an empty scope array as "requires nothing" would make the OR
    /// below satisfied by everybody, so a document that reads as protective would generate a
    /// requirement weaker than declaring none at all.
    /// </remarks>
    [Fact]
    public void AnUnscopedSchemeRequiresAuthenticationRatherThanNothing() {
        var branch = Only(Operation("""      security: [{ key: [] }]"""));

        Assert.Empty(branch.Grants);
        Assert.True(branch.RequiresAuthentication);
    }

    /// <summary>
    /// The array is an OR, and a scoped alternative beside an unscoped one keeps both.
    /// </summary>
    [Fact]
    public void SeparateEntriesAreAlternatives() {
        var operation = Operation(
            """      security: [{ oauth: ["pets:read"] }, { key: [] }]""");

        Assert.Collection(
            operation.AuthorizationBranches,
            first => {
                Assert.Equal(new[] { "pets:read" }, first.Grants);
                Assert.False(first.RequiresAuthentication);
            },
            second => {
                Assert.Empty(second.Grants);
                Assert.True(second.RequiresAuthentication);
            });
    }

    /// <summary>
    /// Two schemes inside one entry are conjoined, so the branch carries both what they require.
    /// </summary>
    [Fact]
    public void SchemesWithinOneEntryAreConjoined() {
        var branch = Only(Operation("""      security: [{ oauth: ["pets:write"], key: [] }]"""));

        Assert.Equal(new[] { "pets:write" }, branch.Grants);
        Assert.True(branch.RequiresAuthentication);
    }

    /// <summary>
    /// An operation that declares nothing inherits the document's default, which is how most
    /// documents express this - once at the top, overridden per operation.
    /// </summary>
    [Fact]
    public void ADocumentLevelDefaultIsInherited() {
        var branch = Only(Operation("", """security: [{ oauth: ["pets:read"] }]"""));

        Assert.Equal(new[] { "pets:read" }, branch.Grants);
    }

    /// <summary>
    /// An operation's own security replaces the document's rather than merging with it.
    /// </summary>
    [Fact]
    public void AnOperationsOwnSecurityReplacesTheDocumentDefault() {
        var branch = Only(Operation(
            """      security: [{ oauth: ["pets:write"] }]""",
            """security: [{ oauth: ["pets:read"] }]"""));

        Assert.Equal(new[] { "pets:write" }, branch.Grants);
    }

    /// <summary>
    /// <c>security: []</c> is the specification's way of opting one operation out of a document-level
    /// default. It derives nothing.
    /// </summary>
    /// <remarks>
    /// Deliberately not <c>[AllowAnonymous]</c>. A described requirement is conjoined with whatever
    /// the handler declared, so it can narrow a route and must not be able to open one; a document
    /// that says "public" cannot be allowed to strip an <c>[AuthorizeGrants]</c> somebody wrote on
    /// the implementation. An author who wants the route anonymous says so in code.
    /// </remarks>
    [Fact]
    public void AnEmptySecurityArrayDerivesNothing() {
        Assert.Empty(
            Operation("""      security: []""", """security: [{ oauth: ["pets:read"] }]""")
                .AuthorizationBranches);
    }

    /// <summary>
    /// A document declaring none anywhere derives nothing.
    /// </summary>
    [Fact]
    public void NoSecurityAnywhereDerivesNothing() {
        Assert.Empty(Operation("").AuthorizationBranches);
    }

    /// <summary>
    /// A misspelled scheme name is reported rather than quietly downgrading the operation.
    /// </summary>
    /// <remarks>
    /// This is the shape of the whole dropped-keyword class: the operation stops requiring the
    /// permission it names and still answers, so the only sign is a request that should have been
    /// refused and was not.
    /// </remarks>
    [Fact]
    public void AnUndeclaredSchemeIsReported() {
        var diagnostics = new List<string>();

        Operation("""      security: [{ ghost: ["pets:read"] }]""", diagnostics: diagnostics);

        Assert.Contains(diagnostics, d => d.Contains("ghost") && d.Contains("listPets"));
    }

    /// <summary>
    /// A scheme the document never declared contributes authentication, never grants.
    /// </summary>
    /// <remarks>
    /// The reference is dangling - the document's own error - and reading its scope list would
    /// invent authorization out of a name that resolves to nothing. Requiring a caller rather than
    /// requiring a permission is the safe reading of a broken document.
    /// </remarks>
    [Fact]
    public void AnUndeclaredSchemeContributesAuthenticationOnly() {
        var branch = Only(Operation("""      security: [{ ghost: ["pets:read"] }]"""));

        Assert.Empty(branch.Grants);
        Assert.True(branch.RequiresAuthentication);
    }
}
