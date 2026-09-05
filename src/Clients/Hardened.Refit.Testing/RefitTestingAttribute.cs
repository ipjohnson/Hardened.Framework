using Hardened.Web.Testing;

namespace Hardened.Refit.Testing;

/// <summary>
/// Builds every Refit client in this test assembly over the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// One line, once, anywhere in the test project:
/// </para>
/// <code>
/// [assembly: RefitTesting]
/// </code>
/// <para>
/// After it, a Refit interface is a test parameter, and a call through one is asserted with
/// <c>Returns&lt;T&gt;()</c>. Nothing has to be written per client, which is the difference between
/// this and an <see cref="ITestClientFactory{TClient}"/>: a Refit client is recognisable by its
/// shape, so a second interface in the same solution costs nothing. A client that needs something
/// this route does not do - its own <c>RefitSettings</c>, a handler of its own in front of the
/// pipeline - still declares a factory, and the factory wins.
/// </para>
/// </remarks>
public sealed class RefitTestingAttribute() : TestClientRouteAttribute(typeof(RefitClientRoute));
