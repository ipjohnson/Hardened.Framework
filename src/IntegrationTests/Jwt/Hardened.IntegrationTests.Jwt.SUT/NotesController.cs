using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Runtime.Authorization;
using Hardened.Web.Runtime.Attributes;

namespace Hardened.IntegrationTests.Jwt.SUT;

/// <summary>What the token said about its caller.</summary>
public record Caller(string? Subject, string? Issuer, string[] Grants);

/// <summary>
/// A route that requires a caller, one that requires a grant, and one that requires nothing.
/// </summary>
public class NotesController
{
    [Get("/me")]
    [Authorize<BearerAuth>]
    public Caller Me(IExecutionContext context) =>
        new(
            context.CallerPrincipal.Subject,
            context.CallerPrincipal.Issuer,
            context.CallerPrincipal.Grants.Order(StringComparer.Ordinal).ToArray()
        );

    [Post("/notes")]
    [AuthorizeGrants("notes:write")]
    public string Write() => "written";

    [Get("/status")]
    public string Status() => "up";
}
