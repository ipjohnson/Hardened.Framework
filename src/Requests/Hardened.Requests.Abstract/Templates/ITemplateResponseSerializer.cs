using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Templates;

/// <summary>
/// Writes a response that names a template, by handing it to an <see cref="ITemplateEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not an <c>IResponseSerializer</c>, though it has the same shape. Registering it as
/// one puts it in the set the locator chooses from, and the locator returns the first registered
/// serializer that claims the context - so a template response whose request also carries
/// <c>Accept: application/json</c> would resolve on registration order against the JSON serializer.
/// Both are registered by one module, so no application could order them differently.
/// </para>
/// <para>
/// <c>IContextSerializationService</c> holds one of these and asks it before it asks the locator
/// anything, which is what makes the answer independent of registration order.
/// </para>
/// </remarks>
public interface ITemplateResponseSerializer {
    bool CanProcessContext(IExecutionContext context);

    Task SerializeResponse(IExecutionContext context);
}
