using Hardened.Web.Testing;

namespace Hardened.Kiota.Testing;

/// <summary>
/// Builds every Kiota client in this test assembly over the pipeline.
/// </summary>
/// <remarks>
/// <para>
/// One line, once, anywhere in the test project:
/// </para>
/// <code>
/// [assembly: KiotaTesting]
/// </code>
/// <para>
/// After it, a client type is a test parameter, and a call through one is asserted with
/// <c>Returns&lt;T&gt;()</c>. Nothing has to be written per client, which is the difference between
/// this and an <see cref="ITestClientFactory{TClient}"/>: a Kiota client is recognisable by its
/// shape, so a second service in the same solution costs nothing. A client that needs something
/// this route does not do - its own authentication provider, a middleware handler - still declares
/// a factory, and the factory wins.
/// </para>
/// </remarks>
public sealed class KiotaTestingAttribute() : TestClientRouteAttribute(typeof(KiotaClientRoute));
