using Hardened.Requests.Abstract.Execution;
using Hardened.Requests.Abstract.RequestFilter;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A handler describes itself with the path it is actually served at.
/// </summary>
/// <remarks>
/// <para>
/// <c>SomeController</c> lives in <c>WebLibrary</c>, which declares <c>[BasePath("/web-library")]</c>
/// on the module and <c>[BasePath("/string-methods")]</c> on the controller. The route answers at
/// <c>/web-library/string-methods/concat/{a}/{b}</c>, and the handler used to report
/// <c>/string-methods/concat/{a}/{b}</c> — the module's prefix belongs to the entry point and never
/// reached the handler class, which is generated from its own declarations.
/// </para>
/// <para>
/// That is not cosmetic. <c>IGlobalFilterRegistry</c>'s per-handler overload is documented as
/// deciding by path, so <c>handlerInfo.Path.StartsWith("/web-library")</c> matched nothing and the
/// filter silently never ran — for every route in every library that owns a URL space, which is
/// the arrangement <c>[BasePath]</c> on a module exists to support.
/// </para>
/// </remarks>
public class HandlerInfoPathTests {

    /// <summary>
    /// Every handler's declared path is one the router would match.
    /// </summary>
    /// <remarks>
    /// Asserted over the whole table rather than one route, because the defect was structural: any
    /// handler reached through a module base path had it, and a test naming a single path would go
    /// on passing if a second library were added with the same fault.
    /// </remarks>
    [HardenedTest]
    public async Task EveryHandlerReportsThePathItIsServedAt(
        ITestWebApp testWebApp, IGlobalFilterRegistry registry) {
        var seen = PathsSeenByAPerHandlerFilter(registry);

        await testWebApp.Get("/web-library/string-methods/concat/a/b");

        Assert.Contains("/web-library/string-methods/concat/{a}/{b}", seen);
        Assert.DoesNotContain("/string-methods/concat/{a}/{b}", seen);
    }

    /// <summary>
    /// The documented pattern, run as written: a filter that gates on a path prefix.
    /// </summary>
    [HardenedTest]
    public async Task APathPrefixFilterMatchesRoutesUnderAModuleBasePath(
        ITestWebApp testWebApp, IGlobalFilterRegistry registry) {
        registry.RegisterFilter(handlerInfo =>
            handlerInfo.Path.StartsWith("/web-library", StringComparison.Ordinal)
                ? new RequestFilterInfo(_ => new StampFilter(), FilterOrder.HandlerCreation)
                : null);

        var underLibrary = await testWebApp.Get("/web-library/string-methods/concat/a/b");

        Assert.True(
            underLibrary.Headers.ContainsKey(StampFilter.HeaderName),
            "a filter gated on the module's base path did not run for a route under it");
    }

    /// <summary>
    /// A handler with no module base path is unchanged — it already reported the right path, and
    /// composing an empty prefix must not give it a different one.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerOutsideAnyModuleBasePathIsUnaffected(
        ITestWebApp testWebApp, IGlobalFilterRegistry registry) {
        var seen = PathsSeenByAPerHandlerFilter(registry);

        await testWebApp.Get("/binding/path/42");

        Assert.Contains("/binding/path/{id}", seen);
    }

    private static List<string> PathsSeenByAPerHandlerFilter(IGlobalFilterRegistry registry) {
        var seen = new List<string>();

        registry.RegisterFilter(handlerInfo => {
            seen.Add(handlerInfo.Path);

            return null;
        });

        return seen;
    }

    private class StampFilter : IExecutionFilter {
        public const string HeaderName = "X-Under-Library";

        public Task Execute(IExecutionChain chain) {
            chain.Context.Response.Headers[HeaderName] = "yes";

            return chain.Next();
        }
    }
}
