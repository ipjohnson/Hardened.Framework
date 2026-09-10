using DependencyModules.Testing.Attributes;
using Hardened.Requests.Abstract.Headers;
using Hardened.Requests.Testing;
using Microsoft.Extensions.Primitives;

namespace Hardened.IntegrationTests.WebApp.SUT.Tests;

/// <summary>
/// A filter declared on a module class, through a built application.
/// </summary>
/// <remarks>
/// <para>
/// <c>WebLibrary</c> carries <c>[ConditionalGet]</c> and <c>SomeController</c>, compiled with it,
/// carries nothing. So the tag on the library's read and the 304 it answers came from the module's
/// declaration and from nowhere else.
/// </para>
/// <para>
/// The host is the other half of the same assertion. It references the library and composes its
/// module, and its own handlers still answer without a tag - because a declaration covers the
/// compilation it was written in, and the host was compiled after the library's document was
/// written.
/// </para>
/// </remarks>
public class ModuleDeclaredFilterTests {

    private const string InTheLibrary = "/web-library/string-methods/concat/hello/world";

    private const string InTheHost = "/binding/path/17";

    private static Action<TestWebRequest> Plain(Action<TestWebRequest>? also = null) =>
        request => {
            request.Headers[KnownHeaders.AcceptEncoding] = new StringValues("identity");
            also?.Invoke(request);
        };

    [HardenedTest]
    public async Task AHandlerCompiledWithTheModuleAnswersWithATag(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(InTheLibrary, Plain());

        Assert.Equal(200, response.StatusCode);
        Assert.True(response.Headers.ContainsKey(KnownHeaders.ETag),
            "The module's declaration should have tagged the read it covers.");
    }

    [HardenedTest]
    public async Task ACallerHoldingThatTagIsAnsweredNotModified(ITestWebApp testWebApp) {
        var first = await testWebApp.Get(InTheLibrary, Plain());

        var tag = first.Headers[KnownHeaders.ETag].ToString();

        var second = await testWebApp.Get(InTheLibrary, Plain(
            request => request.Headers[KnownHeaders.IfNoneMatch] = new StringValues(tag)));

        Assert.Equal(304, second.StatusCode);
        Assert.Equal(0, second.Body.Length);
    }

    /// <summary>
    /// And the host's own reads are untouched, which is the seam a compile-time rung cannot cross.
    /// </summary>
    [HardenedTest]
    public async Task AHandlerInTheHostIsLeftAlone(ITestWebApp testWebApp) {
        var response = await testWebApp.Get(InTheHost, Plain());

        Assert.Equal(200, response.StatusCode);
        Assert.False(response.Headers.ContainsKey(KnownHeaders.ETag),
            "A library module's declaration must not reach the host's handlers.");
    }
}
