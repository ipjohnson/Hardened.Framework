using Hardened.Web.Runtime.Responses;

namespace Hardened.IntegrationTests.Responses.SUT.Tests;

/// <summary>
/// A response type returned on its own, with no declared set around it.
/// </summary>
/// <remarks>
/// <para>
/// <c>Created&lt;T&gt;</c> states its status, its header and which of its members is the body. A
/// declared set reaches all three through the switch the generator emits; a handler returning one
/// thing has no switch, and until the serializer read the value itself nothing asked. It answered
/// 200, with no <c>Location</c>, and the wrapper serialized whole - so the body said
/// <c>"status": 201</c> in a 200 response.
/// </para>
/// <para>
/// Asserted against the thrown spelling rather than against a literal, because the two are the same
/// value and the bug was that they disagreed.
/// </para>
/// </remarks>
public class BareResponseTests {

    private record TodoBody(int Id, string Title);

    [HardenedTest]
    public async Task AReturnedResponseAnswersItsOwnStatus(ITestWebApp app) {
        var response = await app.Post(new NewTodo("bare"), "/responses/bare");

        Assert.Equal(201, response.StatusCode);
    }

    [HardenedTest]
    public async Task AReturnedResponseAppliesItsOwnHeaders(ITestWebApp app) {
        var response = await app.Post(new NewTodo("bare"), "/responses/bare");

        Assert.Equal("/responses/7", response.Headers["Location"].ToString());
    }

    /// <summary>
    /// The payload, not the wrapper that named the status. The wrapper has a public
    /// <c>Value</c>, so if it were serialized <c>Id</c> would read 0.
    /// </summary>
    [HardenedTest]
    public async Task AReturnedResponseSendsItsBodyRatherThanTheContainer(ITestWebApp app) {
        var response = await app.Post(new NewTodo("bare"), "/responses/bare");

        // Read once: the body is a stream, and a second Deserialize finds it consumed.
        var todo = response.Deserialize<TodoBody>();

        Assert.Equal(7, todo.Id);
        Assert.Equal("bare", todo.Title);
    }

    /// <summary>
    /// The whole point: returned and thrown are the same value, so they are the same response.
    /// </summary>
    [HardenedTest]
    public async Task ReturningAndThrowingTheSameValueAnswerIdentically(ITestWebApp app) {
        var returned = await app.Post(new NewTodo("bare"), "/responses/bare");
        var thrown = await app.Post(new NewTodo("bare"), "/responses/bare-thrown");

        Assert.Equal(thrown.StatusCode, returned.StatusCode);
        Assert.Equal(
            thrown.Headers["Location"].ToString(), returned.Headers["Location"].ToString());
        Assert.Equal(await thrown.ReadTextAsync(), await returned.ReadTextAsync());
    }

    /// <summary>
    /// A bodyless type writes nothing, which is what distinguishes a 204 from a 200 carrying the
    /// four characters "null".
    /// </summary>
    [HardenedTest]
    public async Task AReturnedBodylessResponseWritesNoBody(ITestWebApp app) {
        var response = await app.Delete("/responses/bare/1");

        Assert.Equal(204, response.StatusCode);
        Assert.Equal("", await response.ReadTextAsync());
    }

    /// <summary>
    /// A handler that writes a status and then returns a type declaring another gets the type's,
    /// and gets it identically whether or not a set is written around the type.
    /// </summary>
    /// <remarks>
    /// Asserted as an agreement rather than as a number, because the point is that the two
    /// spellings are one behaviour. A set has always resolved this contradiction in favour of the
    /// case; the bare return now does the same rather than differently.
    /// </remarks>
    [HardenedTest]
    public async Task ADeclaredStatusWinsOverOneTheHandlerWrote(ITestWebApp app) {
        var bare = await app.Post(new NewTodo("bare"), "/responses/bare-overridden");
        var inSet = await app.Post(new NewTodo("bare"), "/responses/set-overridden");

        Assert.Equal(inSet.StatusCode, bare.StatusCode);
        Assert.Equal(201, bare.StatusCode);
    }
}
