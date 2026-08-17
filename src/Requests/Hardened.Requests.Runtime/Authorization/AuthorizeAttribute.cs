using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Requires the caller to satisfy <typeparamref name="TPolicy"/>.
/// </summary>
/// <remarks>
/// <para>
/// The hand-authored form: typed, findable by "go to references", and renamed by a refactor along
/// with the policy it names. The composition lives inside the policy, so this attribute never needs
/// a variant per permutation of grants - <c>[AuthorizeAny&lt;A, B, C&gt;]</c> was the wrong shape.
/// </para>
/// <example>
/// <code>
/// [Authorize&lt;CanManagePets&gt;]
/// [Get("/pets/{petId}")]
/// public Task&lt;Pet&gt; GetPet(string petId) =&gt; ...;
/// </code>
/// </example>
/// <para>
/// The policy is constructed once per closed type and its requirement built once, so writing this
/// on a hundred handlers costs one instance and one tree.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeAttribute<TPolicy> : Attribute, IAuthorizeAttribute
    where TPolicy : IAuthorizationPolicy, new() {

    private static readonly TPolicy _policy = new();

    public Requirement Requirement => _policy.Requirement;
}
