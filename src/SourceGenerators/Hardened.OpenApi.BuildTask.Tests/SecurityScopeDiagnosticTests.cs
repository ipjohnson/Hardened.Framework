using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hardened.Generation.Models;
using Hardened.OpenApi.SourceGenerator;
using Xunit;

namespace Hardened.OpenApi.BuildTask.Tests;

/// <summary>
/// Scopes named on a security scheme that has nowhere to put them.
/// </summary>
/// <remarks>
/// <para>
/// OpenAPI gives the scope array a meaning only under <c>oauth2</c> and <c>openIdConnect</c>. Name
/// permissions beside any other scheme and they are read by nothing - so the operation keeps "be
/// authenticated" and loses the permission it asked for. Every caller who can log in passes a check
/// the document says needs a grant: a 403 becomes a 200, and the build is clean.
/// </para>
/// <para>
/// <c>type: http, scheme: bearer</c> is what a real bearer API declares, which is what makes this
/// worth a diagnostic rather than a footnote. It is not an exotic mistake; it is the shape somebody
/// writes when they expect the obvious reading.
/// </para>
/// <para>
/// Reported rather than honoured. Reading the array under <c>http</c> would make Hardened enforce a
/// requirement no other tool in the ecosystem sees, so a document that passed review here would mean
/// something different everywhere else.
/// </para>
/// </remarks>
public class SecurityScopeDiagnosticTests {

    private static string Document(string schemeDeclaration, string requirement) => $$"""
        openapi: 3.0.0
        info: { title: Depot, version: '1.0' }
        paths:
          /products:
            get:
              operationId: listProducts
              security:
                {{requirement}}
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: string
        components:
          securitySchemes:
            {{schemeDeclaration}}
        """;

    private const string Bearer = """
        depotAuth:
              type: http
              scheme: bearer
        """;

    private const string ApiKey = """
        depotAuth:
              type: apiKey
              name: X-Api-Key
              in: header
        """;

    private const string OAuth = """
        depotAuth:
              type: oauth2
              flows:
                clientCredentials:
                  tokenUrl: https://example.invalid/token
                  scopes:
                    "depot:read": Read.
        """;

    private const string NamesAScope = """
        - depotAuth: ["depot:read"]
        """;

    private const string NamesNothing = """
        - depotAuth: []
        """;

    private static (ServiceSpecModel Model, List<string> Diagnostics) Parse(
        string scheme, string requirement) {
        var diagnostics = new List<string>();

        var model = OpenApiSpecParser.Parse(
            Document(scheme, requirement), "depot", CancellationToken.None,
            diagnostics: diagnostics);

        Assert.NotNull(model);

        return (model!, diagnostics);
    }

    private static OperationModel Operation(ServiceSpecModel model) =>
        model.Services.SelectMany(service => service.Operations).Single();

    [Fact]
    public void ScopesOnABearerSchemeAreReported() {
        var (_, diagnostics) = Parse(Bearer, NamesAScope);

        Assert.Contains(diagnostics, d => d.Contains("'depot:read'") && d.Contains("cannot carry"));
    }

    [Fact]
    public void ScopesOnAnApiKeySchemeAreReported() {
        var (_, diagnostics) = Parse(ApiKey, NamesAScope);

        Assert.Contains(diagnostics, d => d.Contains("cannot carry"));
    }

    /// <summary>
    /// The message says what the document said, so the fix does not need a second look at the file.
    /// </summary>
    [Fact]
    public void TheReportNamesTheOperationTheSchemeAndItsType() {
        var (_, diagnostics) = Parse(Bearer, NamesAScope);

        var reported = Assert.Single(diagnostics, d => d.Contains("cannot carry"));

        Assert.Contains("listProducts", reported);
        Assert.Contains("depotAuth", reported);
        Assert.Contains("Http", reported);
    }

    /// <summary>
    /// And the behaviour it is warning about is unchanged: the caller must still authenticate, and
    /// the grant is still not required. A diagnostic that also changed the answer would be a
    /// breaking change wearing a warning.
    /// </summary>
    [Fact]
    public void TheOperationStillRequiresAuthenticationAndNoGrant() {
        var (model, _) = Parse(Bearer, NamesAScope);

        var branch = Assert.Single(Operation(model).AuthorizationBranches);

        Assert.True(branch.RequiresAuthentication);
        Assert.Empty(branch.Grants);
    }

    /// <summary>
    /// A scheme that can carry scopes keeps them, and says nothing.
    /// </summary>
    [Fact]
    public void ScopesOnAnOAuthSchemeAreKeptAndNotReported() {
        var (model, diagnostics) = Parse(OAuth, NamesAScope);

        Assert.DoesNotContain(diagnostics, d => d.Contains("cannot carry"));

        var branch = Assert.Single(Operation(model).AuthorizationBranches);

        Assert.Contains("depot:read", branch.Grants);
    }

    /// <summary>
    /// An empty array on a scheme that cannot carry scopes is the correct way to write it, and must
    /// stay silent - otherwise the diagnostic fires on every API-key API in existence.
    /// </summary>
    [Fact]
    public void AnEmptyScopeArrayIsNotReported() {
        var (_, diagnostics) = Parse(ApiKey, NamesNothing);

        Assert.DoesNotContain(diagnostics, d => d.Contains("cannot carry"));
    }

    /// <summary>
    /// A scheme the document never declared already has its own report - that the reference is
    /// dangling - and must not also collect this one, which would name a type it does not have.
    /// </summary>
    [Fact]
    public void AnUndeclaredSchemeIsReportedOnlyAsDangling() {
        var diagnostics = new List<string>();

        OpenApiSpecParser.Parse(
            Document(Bearer, """
                - missingAuth: ["depot:read"]
                """),
            "depot", CancellationToken.None, diagnostics: diagnostics);

        Assert.Contains(diagnostics, d => d.Contains("does not declare"));
        Assert.DoesNotContain(diagnostics, d => d.Contains("cannot carry"));
    }
}
