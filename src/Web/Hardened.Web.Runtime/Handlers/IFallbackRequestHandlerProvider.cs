namespace Hardened.Web.Runtime.Handlers;

/// <summary>
/// A provider consulted only once every ordinary one has declined.
/// </summary>
/// <remarks>
/// <para>
/// Ordinary providers are consulted in reverse registration order, which is how an application's
/// own route shadows a framework-supplied one. That works while everything is registered by the
/// same module and in a known order, and stops working the moment a provider ships in its own
/// package: whether <c>Hardened.Web.StaticContent</c>'s mount lands before or after the health
/// endpoints then depends on which module the application listed first, which is not something an
/// application should have to think about and not something a package can control.
/// </para>
/// <para>
/// A provider that answers <em>whatever nothing else claimed</em> is a different kind of provider,
/// so it says so in the type system rather than in a comment about registration order. Static
/// content is the case this exists for: a directory of files can shadow any path at all, so it must
/// be asked last, and no amount of ordering care at the registration site can guarantee that across
/// package boundaries.
/// </para>
/// <para>
/// Fallbacks are still consulted in reverse registration order among themselves, so two of them
/// resolve the same way two ordinary providers do.
/// </para>
/// </remarks>
public interface IFallbackRequestHandlerProvider : IWebExecutionRequestHandlerProvider;
