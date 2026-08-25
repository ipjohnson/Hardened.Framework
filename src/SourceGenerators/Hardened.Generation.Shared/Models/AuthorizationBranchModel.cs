using System.Collections.Generic;
using System.Linq;

namespace Hardened.Generation.Models;

/// <summary>
/// One alternative way a caller may satisfy an operation's declared authorization.
/// </summary>
/// <remarks>
/// <para>
/// A branch is an <b>AND</b>: every grant it names is required, and <see cref="RequiresAuthentication"/>
/// is required alongside them. An operation holds a list of these and the list is an <b>OR</b> - which
/// is OpenAPI's own shape, where <c>security</c> is an array of alternatives and the keys within one
/// entry are conjoined.
/// </para>
/// <para>
/// <b>Why authentication is a flag rather than a grant.</b> A scheme that carries no scopes -
/// <c>apiKey</c>, <c>http</c> bearer, or an <c>oauth2</c> entry with an empty array - says the caller
/// must be someone, not that they must hold something. Modelling that as "no grants" would make the
/// branch require nothing, and an OR containing a branch that requires nothing is satisfied by
/// everyone: a document that reads as protective would generate a requirement weaker than having
/// none. It maps to <c>Requirement.Authenticated()</c>, which is a real requirement.
/// </para>
/// </remarks>
internal class AuthorizationBranchModel : IEquatable<AuthorizationBranchModel> {

    /// <summary>The scopes this branch requires, as grant names. All of them, not any.</summary>
    public List<string> Grants { get; set; } = new();

    /// <summary>Whether the caller must be authenticated beyond holding the grants above.</summary>
    public bool RequiresAuthentication { get; set; }

    public bool Equals(AuthorizationBranchModel? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return RequiresAuthentication == other.RequiresAuthentication &&
               Grants.SequenceEqual(other.Grants);
    }

    public override bool Equals(object? obj) => Equals(obj as AuthorizationBranchModel);

    public override int GetHashCode() {
        unchecked {
            var hash = RequiresAuthentication ? 397 : 0;

            foreach (var grant in Grants) {
                hash = (hash * 397) ^ grant.GetHashCode();
            }

            return hash;
        }
    }
}
