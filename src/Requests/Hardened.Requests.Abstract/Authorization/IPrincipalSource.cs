using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// Establishes who a request's caller is, from whatever credential the request carries.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam authentication plugs into. Register one or more implementations and the
/// framework's authentication middleware runs them ahead of the whole handler chain, in
/// registration order, until one answers; the principal it returns is what
/// <see cref="Execution.IExecutionContext.CallerPrincipal"/> holds for the rest of the request,
/// and what <c>AuthorizationFilter</c> judges at both of its positions. An application that
/// registers nothing pays nothing: no middleware is installed and every request stays
/// <see cref="AnonymousCallerPrincipal.Instance"/>, exactly as before.
/// </para>
/// <para>
/// Null means "this request carries nothing of mine" - no <c>Authorization</c> header, no cookie,
/// whatever the source reads - and the next source is asked. A credential that is present and
/// <em>invalid</em> is the source's own decision: return null to let the request continue
/// anonymously and be refused by authorization with a fresh challenge, or throw
/// <see cref="AuthorizationException"/> to refuse it immediately with a specific one.
/// </para>
/// <para>
/// Establishing the caller is all this does. What the caller may do stays where it was:
/// grants resolve through <c>IActivityAuthorizationService</c>'s contributors, and requirements
/// are declared on handlers. A source that also knows the caller's grants may put them on the
/// principal it builds, which is what the testing source does.
/// </para>
/// </remarks>
public interface IPrincipalSource {
    /// <summary>
    /// The caller this request's credential establishes, or null when the request carries no
    /// credential this source reads.
    /// </summary>
    ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context);
}

/// <summary>
/// A principal source for one declared authentication scheme.
/// </summary>
/// <remarks>
/// <para>
/// The scheme type is the same one <c>[Authorize&lt;TScheme&gt;]</c> names and the published
/// document keys its <c>securitySchemes</c> entry by, so "find references" connects the
/// operations requiring a scheme with the source that implements it. The runtime does not
/// dispatch on the type parameter - every registered source is asked in order - so implementing
/// the plain <see cref="IPrincipalSource"/> works identically; this form exists to state the
/// tie.
/// </para>
/// <para>
/// A registration attribute registers a class as the interface it declares, so a source written
/// this way is in the container under the closed generic rather than under
/// <see cref="IPrincipalSource"/>. <c>AuthenticationStartupService</c> collects both, in one
/// registration order, which is what makes "works identically" true of registration as well as
/// of dispatch.
/// </para>
/// </remarks>
public interface IPrincipalSource<TScheme> : IPrincipalSource
    where TScheme : IAuthenticationScheme;
