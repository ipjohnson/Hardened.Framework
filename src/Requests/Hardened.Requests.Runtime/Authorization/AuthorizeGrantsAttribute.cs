using Hardened.Requests.Abstract.Authorization;
using Combinator = Hardened.Requests.Abstract.Authorization.Requirement;

namespace Hardened.Requests.Runtime.Authorization;

/// <summary>
/// Requires the grants named.
/// </summary>
/// <remarks>
/// <para>
/// Every grant named here is required, and every authorization attribute on a handler is required.
/// Writing more of them can only narrow what is admitted, never widen it - which is what makes the
/// attribute safe to inherit, safe to write on a controller and a method at once, and safe for a
/// convention to add to.
/// </para>
/// <example>
/// <code>
/// [AuthorizeGrants("pets:read", "pets:write")]   // both required
/// [AuthorizeGrants("tenant:member")]             // ...and this as well
/// </code>
/// </example>
/// <para>
/// <b>Not sealed, because deriving from it is one of the hand-authored forms.</b> A named attribute
/// reads better at the call site than a string does, is found by "go to references", and is renamed
/// by a refactor - so the grant vocabulary can be written once and spelled everywhere as a type:
/// </para>
/// <example>
/// <code>
/// public sealed class RequiresPetWriteAttribute : AuthorizeGrantsAttribute {
///     public RequiresPetWriteAttribute() : base(Grants.Pets.Read, Grants.Pets.Write) { }
/// }
/// </code>
/// </example>
/// <para>
/// <see cref="AuthorizeGrantsAttribute{T}"/> is the other, and wants one type rather than one type
/// per attribute. Both end at the same place - see that type's remarks.
/// </para>
/// <para>
/// The string form remains what a generator emits from a specification, where a human never reads
/// the attribute and the values cannot be a typo because the generator read them out of the spec.
/// </para>
/// <para>
/// Alternatives - "this grant <em>or</em> that one" - are not expressible by stacking attributes,
/// deliberately. They belong inside a single <see cref="IAuthorizationPolicy"/>, where one author
/// writes one expression, rather than emerging from how many attributes happen to be present.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorizeGrantsAttribute : Attribute, IAuthorizeAttribute {
    public AuthorizeGrantsAttribute(params string[] grants) {
        if (grants is null || grants.Length == 0) {
            throw new ArgumentException(
                "[AuthorizeGrants] must name at least one grant. An empty one would require nothing " +
                "while looking like it requires something, which is the one failure mode an " +
                "authorization attribute must not have. Use [AllowAnonymous] to make an operation " +
                "public on purpose.",
                nameof(grants));
        }

        Grants = grants;
        Requirement = Combinator.AllOf(Array.ConvertAll(grants, Combinator.Grant));
    }

    /// <summary>The grants named, in the order they were written.</summary>
    public IReadOnlyList<string> Grants { get; }

    public Requirement Requirement { get; }
}

/// <summary>
/// A named set of grants, declared once and required wherever it is named.
/// </summary>
/// <remarks>
/// <para>
/// The vocabulary an application authorizes in, as a type rather than as strings repeated at call
/// sites. What the set contains is written in one place, so widening or narrowing it is one edit
/// and every handler requiring it follows.
/// </para>
/// <example>
/// <code>
/// public sealed class PetsReadWrite : IGrantProvider {
///     public string[] Grants => [Grants.Pets.Read, Grants.Pets.Write];
/// }
/// </code>
/// </example>
/// <para>
/// Read once, when the attribute naming it is constructed - which happens in the generated
/// handler's static initializer, so a provider is instantiated once per handler type and never on a
/// request. It follows that the set must be a constant of the application rather than something
/// read from configuration or a store: nothing re-reads it, and a grant that can change at run time
/// belongs behind <see cref="IActivityAuthorizationHandler"/>, which exists for exactly that.
/// </para>
/// </remarks>
public interface IGrantProvider {
    /// <summary>Every grant in the set. All of them are required.</summary>
    string[] Grants { get; }
}

/// <summary>
/// Requires the grants <typeparamref name="T"/> names.
/// </summary>
/// <remarks>
/// <para>
/// The typed spelling of <see cref="AuthorizeGrantsAttribute"/>. One
/// <see cref="IGrantProvider"/> describes a set of grants, and every handler needing that set names
/// the same type:
/// </para>
/// <example>
/// <code>
/// [AuthorizeGrants&lt;PetsReadWrite&gt;]
/// [Post("/pets")]
/// public Task&lt;Pet&gt; Create(Pet pet) => ...;
/// </code>
/// </example>
/// <para>
/// <b>Where it wins over deriving an attribute.</b> A derived attribute is a type per <em>call-site
/// spelling</em>; this is a type per <em>set of grants</em>, reused by every handler that needs it.
/// Nothing is generated, nothing is registered, and the grants are a compile-time reference rather
/// than a string - so a renamed constant is a build error rather than a 403 in staging.
/// </para>
/// <para>
/// <b>Lots of ways in, one way out.</b> Strings from a specification, a derived attribute, this,
/// <c>[Authorize&lt;TPolicy&gt;]</c>, and an <c>IAuthorizationConvention</c> are five ways to say
/// what a handler needs, and they exist because they suit different situations - generated versus
/// hand-written, one route versus a whole class of them. They converge before anything acts on
/// them: each produces a <see cref="Requirement"/>, every requirement on a handler is conjoined
/// into the one on <c>IExecutionRequestHandlerInfo</c>, and the authorization filter reads only
/// that. There is one thing to reason about at the point it matters, however it was written.
/// </para>
/// <para>
/// It conjoins like every other authorization attribute, so naming two sets requires both:
/// </para>
/// <example>
/// <code>
/// [AuthorizeGrants&lt;PetsReadWrite&gt;]
/// [AuthorizeGrants&lt;TenantMember&gt;]   // ...and this as well
/// </code>
/// </example>
/// </remarks>
/// <typeparam name="T">
/// The set of grants required. Constructed once, when this attribute is.
/// </typeparam>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class AuthorizeGrantsAttribute<T> : Attribute, IAuthorizeAttribute where T : IGrantProvider, new() {
    public AuthorizeGrantsAttribute() {
        var grants = new T().Grants;

        // Checked rather than left to AllOf, which would throw about "a requirement" naming no
        // conditions - true, and no help at all in finding the provider that returned nothing. This
        // runs in a generated handler's static initializer, so the exception a developer actually
        // sees is a TypeInitializationException wrapping whatever is thrown here; the inner message
        // is the only part naming the cause.
        if (grants is null || grants.Length == 0) {
            throw new ArgumentException(
                $"[AuthorizeGrants<{typeof(T).Name}>] requires at least one grant, and " +
                $"{typeof(T).Name}.Grants returned none. An empty set would require nothing while " +
                "looking like it requires something, which is the one failure mode an authorization " +
                "attribute must not have. Use [AllowAnonymous] to make an operation public on purpose.");
        }

        Grants = grants;
        Requirement = Combinator.AllOf(Array.ConvertAll(grants, Combinator.Grant));
    }

    /// <summary>The grants named, in the order the provider returned them.</summary>
    public IReadOnlyList<string> Grants { get; }

    public Requirement Requirement { get; }
}
