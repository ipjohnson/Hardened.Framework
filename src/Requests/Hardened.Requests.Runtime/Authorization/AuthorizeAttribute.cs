using Hardened.Requests.Abstract.Authorization;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Requires a caller established via <typeparamref name="TAuth"/>.
/// </summary>
/// <remarks>
/// <para>
/// The single type parameter is the authentication scheme, not the policy. It used to be the
/// policy; the policy now rides second, in <see cref="AuthorizeAttribute{TAuth,TPolicy}"/>,
/// because an operation's scheme is the fact every secured operation has and most only have -
/// "an authenticated caller" needed no spelling at all before this - and because the published
/// document needs the scheme named to declare anything. A policy type written here fails the
/// constraint at the call site, which names <see cref="IAuthenticationScheme"/> and is the
/// migration message: move the policy to the second position.
/// </para>
/// <example>
/// <code>
/// [HttpAuthenticationScheme("bearer")]
/// public sealed class BearerAuth : IAuthenticationScheme;
///
/// [Authorize&lt;BearerAuth&gt;]
/// [Post("/pets")]
/// public Task&lt;Pet&gt; CreatePet(CreatePetRequest body) =&gt; ...;
/// </code>
/// </example>
/// <para>
/// Using a scheme anywhere is what declares it: the generator collects every
/// <typeparamref name="TAuth"/> the handlers name into <c>components.securitySchemes</c>, and
/// the operation carries its requirement. Enforcement is
/// <see cref="Requirement.Authenticated"/> - which issuer and token shape the scheme means is
/// the application's authentication middleware's business, exactly as it was.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeAttribute<TAuth> : Attribute, IAuthorizeAttribute
    where TAuth : IAuthenticationScheme {

    public Requirement Requirement => Requirement.Authenticated();
}

/// <summary>
/// Requires a caller established via <typeparamref name="TAuth"/> who satisfies
/// <typeparamref name="TPolicy"/>.
/// </summary>
/// <remarks>
/// <para>
/// The hand-authored composition form: typed, findable by "go to references", and renamed by a
/// refactor along with the policy it names. The composition lives inside the policy, so this
/// attribute never needs a variant per permutation of grants -
/// <c>[AuthorizeAny&lt;A, B, C&gt;]</c> was the wrong shape.
/// </para>
/// <example>
/// <code>
/// [Authorize&lt;BearerAuth, CanManagePets&gt;]
/// [Get("/pets/{petId}")]
/// public Task&lt;Pet&gt; GetPet(string petId) =&gt; ...;
/// </code>
/// </example>
/// <para>
/// The policy is constructed once per closed type and its requirement built once, so writing this
/// on a hundred handlers costs one instance and one tree. The requirement conjoins
/// <see cref="Requirement.Authenticated"/> with the policy's own, which is what the pipeline
/// would do anyway for a policy over grants and makes a context-only policy still demand a
/// caller.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AuthorizeAttribute<TAuth, TPolicy> : Attribute, IAuthorizeAttribute
    where TAuth : IAuthenticationScheme
    where TPolicy : IAuthorizationPolicy, new() {

    private static readonly Requirement _requirement =
        Requirement.Authenticated() & new TPolicy().Requirement;

    public Requirement Requirement => _requirement;
}
