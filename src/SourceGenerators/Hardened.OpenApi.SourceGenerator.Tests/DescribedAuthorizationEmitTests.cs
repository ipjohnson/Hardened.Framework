using Hardened.SourceGeneration.Testing;
using Xunit;

namespace Hardened.OpenApi.SourceGenerator.Tests;

/// <summary>
/// The <c>Requirement</c> expression a described operation's handler carries.
/// </summary>
/// <remarks>
/// Written out rather than reduced to a canonical form, because this lands in a generated file
/// somebody will read while working out why a request was refused. One grant emits
/// <c>Grant("x")</c>, not <c>AnyOf(AllOf(Grant("x")))</c>.
/// </remarks>
public class DescribedAuthorizationEmitTests {

    private static string Handler(string security) {
        var result = OpenApiGenerator.Run(
            $$"""
              openapi: "3.0.0"
              info: { title: Pets, version: "1.0" }
              paths:
                /pets:
                  get:
                    tags: [Pet]
                    operationId: listPets
              {{security}}
                    responses:
                      '200':
                        description: A pet
                        content:
                          application/json:
                            schema:
                              $ref: '#/components/schemas/Pet'
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
              """).AssertNoErrors();

        return string.Join(
            "\n",
            result.GeneratedSources
                .Where(pair => pair.Key.Contains("ListPets"))
                .Select(pair => pair.Value));
    }

    private const string Prefix = "global::Hardened.Requests.Abstract.Authorization.Requirement";

    /// <summary>One grant emits one term, with no wrapper around it.</summary>
    [Fact]
    public void OneGrantEmitsOneTerm() {
        var handler = Handler("""      security: [{ oauth: ["pets:read"] }]""");

        Assert.Contains(
            "new global::Hardened.Requests.Runtime.Authorization.DescribedAuthorization(" +
            Prefix + ".Grant(\"pets:read\")",
            handler);

        Assert.DoesNotContain(".AllOf(", handler);
        Assert.DoesNotContain(".AnyOf(", handler);
    }

    /// <summary>Several grants on one scheme are conjoined.</summary>
    [Fact]
    public void SeveralGrantsAreConjoined() {
        Assert.Contains(
            Prefix + ".AllOf(" + Prefix + ".Grant(\"pets:read\"), " + Prefix + ".Grant(\"pets:write\"))",
            Handler("""      security: [{ oauth: ["pets:read", "pets:write"] }]"""));
    }

    /// <summary>
    /// Alternatives are an OR, and an unscoped one becomes <c>Authenticated()</c> rather than being
    /// dropped - which would leave an OR that anybody satisfies.
    /// </summary>
    [Fact]
    public void AlternativesBecomeAnOrAndAnUnscopedOneRequiresACaller() {
        Assert.Contains(
            Prefix + ".AnyOf(" + Prefix + ".Grant(\"pets:read\"), " + Prefix + ".Authenticated())",
            Handler("""      security: [{ oauth: ["pets:read"] }, { key: [] }]"""));
    }

    /// <summary>
    /// It sits in the handler's metadata beside whatever else is there, which is what makes it
    /// compose with an attribute rather than replace one.
    /// </summary>
    [Fact]
    public void ItIsCarriedAsHandlerMetadata() {
        Assert.Contains(
            "_metadata", Handler("""      security: [{ oauth: ["pets:read"] }]"""));
    }

    /// <summary>A description declaring none emits none.</summary>
    [Fact]
    public void NoDeclaredSecurityEmitsNothing() {
        Assert.DoesNotContain("DescribedAuthorization", Handler(""));
    }

    /// <summary>
    /// <c>security: []</c> emits nothing either - it opts out of a default rather than declaring the
    /// route anonymous, and a described requirement may never remove one.
    /// </summary>
    [Fact]
    public void AnEmptySecurityArrayEmitsNothing() {
        Assert.DoesNotContain("DescribedAuthorization", Handler("""      security: []"""));
    }
}
