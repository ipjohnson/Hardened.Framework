using Hardened.Idl.Models;
using Xunit;

namespace Hardened.Smithy.BuildTask.Tests;

/// <summary>
/// Equality on a described authorization branch.
/// </summary>
/// <remarks>
/// <para>
/// Not ceremony. <c>OperationModel.Equals</c> compares these, and the incremental generator compares
/// <c>OperationModel</c> to decide whether anything has to be regenerated - so a branch that
/// compared equal to a different branch would leave the previous build's authorization in place
/// after somebody changed a scope. The failure is a route still guarded by the requirement it used
/// to have, which nothing else would report.
/// </para>
/// <para>
/// Here rather than beside the OpenAPI tests because the model is compiled into every front end
/// separately: a test in one project exercises that project's copy and no other.
/// </para>
/// </remarks>
public class AuthorizationBranchModelTests {

    private static AuthorizationBranchModel Branch(bool authenticated, params string[] grants) =>
        new() { RequiresAuthentication = authenticated, Grants = [.. grants] };

    [Fact]
    public void BranchesNamingTheSameGrantsAreEqual() {
        Assert.Equal(Branch(false, "pets:read"), Branch(false, "pets:read"));
    }

    [Fact]
    public void EqualBranchesHashEqual() {
        Assert.Equal(
            Branch(false, "pets:read", "pets:write").GetHashCode(),
            Branch(false, "pets:read", "pets:write").GetHashCode());
    }

    [Fact]
    public void ABranchEqualsItself() {
        var branch = Branch(true);

        Assert.True(branch.Equals(branch));
    }

    [Fact]
    public void ABranchDoesNotEqualNull() {
        Assert.False(Branch(true).Equals(null));
        Assert.False(Branch(true).Equals((object?)null));
    }

    [Fact]
    public void ABranchDoesNotEqualAnotherType() {
        Assert.False(Branch(true).Equals("pets:read"));
    }

    /// <summary>
    /// A different grant is a different requirement.
    /// </summary>
    [Fact]
    public void DifferentGrantsAreNotEqual() {
        Assert.NotEqual(Branch(false, "pets:read"), Branch(false, "pets:write"));
    }

    /// <summary>
    /// So is one more grant. An AND that gained a term admits strictly fewer callers.
    /// </summary>
    [Fact]
    public void AnExtraGrantIsNotEqual() {
        Assert.NotEqual(Branch(false, "pets:read"), Branch(false, "pets:read", "pets:write"));
    }

    /// <summary>
    /// Order matters, because the emitted expression is written in it and a generated file that
    /// reshuffles between builds defeats the up-to-date check on the target.
    /// </summary>
    [Fact]
    public void GrantOrderIsPartOfIdentity() {
        Assert.NotEqual(
            Branch(false, "pets:read", "pets:write"),
            Branch(false, "pets:write", "pets:read"));
    }

    /// <summary>
    /// The authentication flag is part of identity on its own - it is the difference between
    /// "requires a caller" and "requires nothing", which is the distinction the whole design turns
    /// on.
    /// </summary>
    [Fact]
    public void TheAuthenticationFlagIsPartOfIdentity() {
        Assert.NotEqual(Branch(true), Branch(false));
        Assert.NotEqual(Branch(true, "pets:read"), Branch(false, "pets:read"));
    }

    /// <summary>
    /// Two empty branches compare equal, which is what lets an unchanged model skip regeneration.
    /// </summary>
    [Fact]
    public void EmptyBranchesAreEqual() {
        Assert.Equal(Branch(false), Branch(false));
        Assert.Equal(Branch(false).GetHashCode(), Branch(false).GetHashCode());
    }
}
