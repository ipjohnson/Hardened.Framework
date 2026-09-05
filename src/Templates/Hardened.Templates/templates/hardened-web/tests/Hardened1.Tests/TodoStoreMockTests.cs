#if (specFirst)
using Hardened1.Models;
#endif
#if (nsubstitute)
using NSubstitute;
#endif
#if (moq)
using Moq;
#endif
#if (fakeiteasy)
using FakeItEasy;
#endif

namespace Hardened1.Tests;

/// <summary>
/// A mock behind a route. The store is replaced for the whole application, so the handler resolves
/// the double the test configures - one instance, with nothing in the application's wiring changed.
/// </summary>
/// <remarks>
#if (moq)
/// A Mock&lt;T&gt; parameter is the mock to configure, and the container is given its Object; a
/// parameter typed as the service and marked [Mock] receives that Object instead.
#else
/// [Mock] on a parameter registers the double over the application's own registration and hands
/// the test the same instance, so what the test configures is what the handler was built against.
#endif
/// ITestWebApp sends the request through the pipeline, so this reads the same whichever client the
/// project generates.
/// </remarks>
public class TodoStoreMockTests {

    /// <summary>The response shape as a client sees it, asserted on the wire rather than on an internal type.</summary>
    private record TodoResponse(int Id, string Title, bool Done);

    [HardenedTest]
#if (moq)
    public async Task GetTodo_ReadsTheMockedStore(ITestWebApp app, Mock<ITodoStore> store) {
        store.Setup(s => s.Find(1)).ReturnsAsync(new Todo(1, "from the mock", false));
#else
    public async Task GetTodo_ReadsTheMockedStore(ITestWebApp app, [Mock] ITodoStore store) {
#if (nsubstitute)
        store.Find(1).Returns(new Todo(1, "from the mock", false));
#endif
#if (fakeiteasy)
        A.CallTo(() => store.Find(1)).Returns(new Todo(1, "from the mock", false));
#endif
#endif

        var response = await app.Get("/todos/1");

        response.Assert.Ok();
#if (xunit)
        Assert.Equal("from the mock", response.Deserialize<TodoResponse>().Title);
#else
        Assert.That(response.Deserialize<TodoResponse>().Title, Is.EqualTo("from the mock"));
#endif
    }
}
