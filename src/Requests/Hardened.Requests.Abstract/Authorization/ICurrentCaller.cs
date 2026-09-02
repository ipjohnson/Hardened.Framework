namespace Hardened.Requests.Abstract.Authorization;

/// <summary>
/// The caller of the request being handled, as a service a handler can take.
/// </summary>
/// <remarks>
/// <para>
/// <c>IExecutionContext.CallerPrincipal</c> is where the principal lives, and a code-first handler
/// reaches it by taking an <see cref="Execution.IExecutionContext"/> parameter. A specification-first
/// handler implements a generated interface, so its signature is fixed and that escape does not
/// exist - which left ownership checks and grant-dependent filtering unwritable without
/// hand-rolled plumbing. Both spec-first arms of the 0.18 trial invented the same scoped holder
/// independently; this is it, shipped.
/// </para>
/// <code>
/// [Handler]
/// public class OrderService(ICurrentCaller caller, IOrderStore store) : IOrderService {
///     public async Task&lt;Order&gt; GetOrder(string orderId) {
///         var order = await store.Get(orderId);
///
///         if (order.OwnerSubject != caller.Principal.Subject) {
///             throw new ForbiddenException();
///         }
///
///         return order;
///     }
/// }
/// </code>
/// <para>
/// Scoped, and present on every request. A request no principal source answered for carries
/// <see cref="AnonymousCallerPrincipal.Instance"/>, so a handler reads the same shape either way
/// and never checks for null.
/// </para>
/// <para>
/// Registered by <c>HardenedRequestModule</c>, so plain constructor injection works with nothing
/// wired by hand. It is not a substitute for <c>[Authorize]</c> and its friends: what a caller may
/// do is declared on the operation and judged before the handler runs. This is for the decisions
/// only the handler can make - whether this caller owns this row.
/// </para>
/// </remarks>
public interface ICurrentCaller {
    /// <summary>
    /// The caller this request established, or <see cref="AnonymousCallerPrincipal.Instance"/>.
    /// </summary>
    ICallerPrincipal Principal { get; }
}
