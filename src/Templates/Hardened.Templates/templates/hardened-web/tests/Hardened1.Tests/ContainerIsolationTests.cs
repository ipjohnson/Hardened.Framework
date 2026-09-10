namespace Hardened1.Tests;

/// <summary>
/// Two requests in one test do not share a container.
/// </summary>
/// <remarks>
/// TodoStore is a [SingletonService] holding its todos in a field, so "singleton" is exactly the
/// question these two tests answer. A todo one request creates is gone by the next, because the
/// next request built a container of its own and a store of its own with it.
///
/// That is the deployment model rather than a harness detail. An execution environment is not
/// promised between invocations - two queue handlers deployed as two functions are two processes,
/// and a service behind a load balancer is many instances - so a handler leaning on what the last
/// request left behind fails here rather than intermittently in production.
///
/// [PipelineHost] because this is the check the pipeline can make. A socket host hands the test an
/// HttpClient bound to one port, so it reuses one container and reports ContainerPolicy.Reused;
/// the same handlers run here for the isolation check. See "Testing" in README.md.
/// </remarks>
[PipelineHost]
public class ContainerIsolationTests {

    private record TodoResponse(int Id, string Title, bool Done);

    private record NewTodoRequest(string Title);

    [HardenedTest]
    public async Task ATodoOneRequestCreatesIsGoneByTheNext(ITestWebApp app) {
        (await app.Post(new NewTodoRequest("Write a test"), "/todos")).Assert.Ok();

        var todos = (await app.Get("/todos")).Deserialize<List<TodoResponse>>();

#if (xunit)
        Assert.Equal([1, 2], todos.Select(todo => todo.Id));
#else
        Assert.That(todos.Select(todo => todo.Id), Is.EqualTo(new[] { 1, 2 }));
#endif
    }

    /// <summary>
    /// [Shared] is the escape hatch, and it belongs on a test whose subject is the reuse itself.
    /// </summary>
    /// <remarks>
    /// It marks the way the parameter is built rather than the object the test holds: pinning the
    /// object alone would change nothing about which container its requests reach, because the
    /// host decides that. Reach for it for a cache serving a second read or one budget across
    /// several attempts, and not to make an ordinary test pass.
    /// </remarks>
    [HardenedTest]
    public async Task SharedSendsEveryRequestToOneContainer([Shared] ITestWebApp app) {
        (await app.Post(new NewTodoRequest("Write a test"), "/todos")).Assert.Ok();

        var todos = (await app.Get("/todos")).Deserialize<List<TodoResponse>>();

#if (xunit)
        Assert.Equal([1, 2, 3], todos.Select(todo => todo.Id));
#else
        Assert.That(todos.Select(todo => todo.Id), Is.EqualTo(new[] { 1, 2, 3 }));
#endif
    }
}
