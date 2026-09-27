using System.Net.Http.Json;
using DependencyModules.Testing.Attributes;
using DependencyModules.xUnit.Attributes;
using Hardened.IntegrationTests.LambdaHttp.SUT;
using Hardened.Web.Testing;
using Xunit;

namespace Hardened.IntegrationTests.LambdaHttp.SUT.Tests;

/// <summary>
/// An ordinary <c>HttpClient</c> against the Lambda web host.
/// </summary>
/// <remarks>
/// <para>
/// The suite drives <see cref="ITestWebApp"/> everywhere else, which reaches the host directly. A
/// client goes the other way, through the message handler the host hands out, and that is a
/// separate path with its own request translation - so a client that worked on the pipeline and not
/// on Lambda would have gone unnoticed.
/// </para>
/// <para>
/// It also says the portability claim in a second voice. Nothing here mentions Lambda; the same
/// test passes on the pipeline and over a socket, and only <c>[LambdaWebTesting]</c> in
/// <c>Bootstrap.cs</c> decides which.
/// </para>
/// </remarks>
public class HostClientTests
{
    [ModuleTest]
    public async Task AClientReachesTheHandlerThroughTheHost(HttpClient client)
    {
        var order = await client.GetFromJsonAsync<Order>("/orders/c-1");

        Assert.NotNull(order);
        Assert.Equal("c-1", order!.Id);
    }

    /// <summary>
    /// <c>Content-Type</c> reaches the client, on the content where a client reads it. It was
    /// dropped, and a generated client took the missing type to mean there was no body.
    /// </summary>
    [ModuleTest]
    public async Task AResponseCarriesItsContentType(HttpClient client)
    {
        using var response = await client.GetAsync(
            "/orders/c-1",
            TestContext.Current.CancellationToken
        );

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    /// <summary>
    /// A request's <c>Content-Type</c> reaches the function too. A multipart body is unreadable
    /// without the boundary the type carries, and binary content is sent as base64 only when its
    /// type says it is not text.
    /// </summary>
    [ModuleTest]
    public async Task ARequestKeepsItsContentType(HttpClient client)
    {
        using var file = new ByteArrayContent([0x00, 0xFF, 0x10, 0x80]);

        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(
            "application/octet-stream"
        );

        using var form = new MultipartFormDataContent { { file, "file", "bytes.bin" } };

        using var response = await client.PostAsync(
            "/uploads",
            form,
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            "\"00FF1080\"",
            await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)
        );
    }

    /// <summary>
    /// Every request builds an environment of its own, which is what a deployed function is not
    /// promised to avoid.
    /// </summary>
    /// <remarks>
    /// Asserted through the answers rather than by looking at containers, because a container is not
    /// something a test holds. Both calls answer, which is the whole claim: nothing the first
    /// invocation left behind was needed by the second.
    /// </remarks>
    [ModuleTest]
    public async Task EachRequestGetsItsOwnEnvironment(HttpClient client)
    {
        var first = await client.GetFromJsonAsync<Order>("/orders/c-1");
        var second = await client.GetFromJsonAsync<Order>("/orders/c-2");

        Assert.Equal("c-1", first!.Id);
        Assert.Equal("c-2", second!.Id);
    }

    /// <summary>
    /// A client marked <c>[Shared]</c> sends every request to the container the test was resolved
    /// from, which is the warm sandbox a test whose subject is the reuse asks for.
    /// </summary>
    [ModuleTest]
    public async Task ASharedClientStillAnswers([Shared] HttpClient client)
    {
        var order = await client.GetFromJsonAsync<Order>("/orders/c-3");

        Assert.Equal("c-3", order!.Id);
    }

    /// <summary>
    /// The host says which deployment it stands for, and on Lambda that is one environment per
    /// invocation.
    /// </summary>
    /// <remarks>
    /// Read by a person rather than by the harness, which is why it is asserted: a property nothing
    /// consumes is a property that can drift away from what the host actually does. The claim is
    /// about the deployment, not a preference - a sandbox is not promised between invocations, so a
    /// handler leaning on what the last one left behind has to fail here.
    /// </remarks>
    [ModuleTest]
    public void TheHostRebuildsItsContainerPerInvocation(ITestHost host)
    {
        Assert.Equal(TestContainerPolicy.PerInvocation, host.ContainerPolicy);
    }
}
