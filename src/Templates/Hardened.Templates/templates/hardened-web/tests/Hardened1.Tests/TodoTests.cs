namespace Hardened1.Tests;

/// <summary>
/// ITestWebApp sends a request through the real pipeline - routing, filters, binding, the handler
/// and serialisation - with no socket, port or running host.
/// </summary>
/// <remarks>
/// Every declared status is asserted, not only the happy one. A response model that is only ever
/// exercised at 200 is indistinguishable from one that has no declared set at all, which is the
/// thing these tests exist to tell apart.
/// </remarks>
public class TodoTests {

    /// <summary>
    /// The response shapes as a client sees them, declared here rather than reused from the
    /// application - so these tests read the same whether the models were hand-written or generated
    /// from a contract, and assert on the wire rather than on an internal type.
    /// </summary>
    private record TodoResponse(int Id, string Title, bool Done);

    private record NewTodoRequest(string Title);

    [HardenedTest]
    public async Task ListTodos_ReturnsEveryTodo(ITestWebApp app) {
        var response = await app.Get("/todos");

        response.Assert.Ok();

        Assert.Equal(
            [1, 2], response.Deserialize<List<TodoResponse>>().Select(todo => todo.Id));
    }

    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(ITestWebApp app) {
        var response = await app.Get("/todos/1");

        response.Assert.Ok();

        Assert.Equal(1, response.Deserialize<TodoResponse>().Id);
    }

    [HardenedTest]
    public async Task GetTodo_UnknownId_IsNotFound(ITestWebApp app) {
        (await app.Get("/todos/9999")).Assert.NotFound();
    }

    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsConflict(ITestWebApp app) {
        var response = await app.Post(new NewTodoRequest("Add an endpoint"), "/todos");

        Assert.Equal(409, response.StatusCode);
    }

    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsNotFound(ITestWebApp app) {
        (await app.Delete("/todos/9999")).Assert.NotFound();
    }

#if (standardMode)
    /// <summary>
    /// 200, not 201 - and that is the point of the assertion rather than an oversight.
    /// </summary>
    /// <remarks>
    /// Standard mode names one success type per handler and has no way to put a status beside it,
    /// so a created todo comes back at 200. Generate this template with --response-model response
    /// and the same route answers 201 with a Location header, because the status moved into the
    /// signature. This test is what makes that difference visible rather than a claim in a comment.
    /// </remarks>
    [HardenedTest]
    public async Task CreateTodo_AnswersTwoHundred(ITestWebApp app) {
        var response = await app.Post(new NewTodoRequest("Write a test"), "/todos");

        response.Assert.Ok();
    }

    /// <summary>200 with the removed todo, for the same reason.</summary>
    [HardenedTest]
    public async Task RemoveTodo_AnswersTwoHundred(ITestWebApp app) {
        (await app.Delete("/todos/1")).Assert.Ok();
    }
#endif
#if (declaredMode)
    /// <summary>201 and a Location header, both declared in the response set.</summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersCreatedWithALocation(ITestWebApp app) {
        var response = await app.Post(new NewTodoRequest("Write a test"), "/todos");

        Assert.Equal(201, response.StatusCode);
    }

    /// <summary>
    /// 204, and no body with it.
    /// </summary>
    /// <remarks>
    /// The case carries nothing, and the generated dispatch is told not to serialise it - so this
    /// is an empty body rather than the four characters "null".
    /// </remarks>
    [HardenedTest]
    public async Task RemoveTodo_AnswersNoContent(ITestWebApp app) {
        var response = await app.Delete("/todos/1");

        Assert.Equal(204, response.StatusCode);
    }
#endif
#if (specFirst)
    /// <summary>
    /// The constraints in the contract are enforced before the handler runs.
    /// </summary>
    /// <remarks>
    /// Nothing in this project validates anything. maxLength on the title became a filter in front
    /// of the generated handler, so a value too long never reaches the code.
    /// </remarks>
    [HardenedTest]
    public async Task AValueTheContractDisallowsIsRejected(ITestWebApp app) {
        var response = await app.Post(new NewTodoRequest(new string('x', 100)), "/todos");

        Assert.Equal(400, response.StatusCode);
    }
#endif
}
