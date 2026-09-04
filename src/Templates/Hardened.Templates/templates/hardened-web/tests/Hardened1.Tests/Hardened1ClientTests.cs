namespace Hardened1.Tests;

/// <summary>
/// The generated client, driven through the real pipeline with no socket.
/// </summary>
/// <remarks>
/// The client is a test parameter; TestClients.cs says how it is built. Refusals are asserted with
/// Assert.ThrowsAsync in the client's own vocabulary, and what the client does not surface - the
/// status it did not throw on, the headers that came with it - is read from LastResponse, which
/// the transport keeps for the current test.
/// </remarks>
public class TemplateModuleNameClientTests {

    [HardenedTest]
    public async Task ListTodos_ThroughTheGeneratedClient(TemplateModuleNameClient client) {
        var todos = await client.Todos.GetAsync();

        Assert.Equal([1, 2], todos!.Select(todo => todo.Id!.Value));
    }

    [HardenedTest]
    public async Task GetTodo_ThroughTheGeneratedClient(TemplateModuleNameClient client) {
        var todo = await client.Todos[1].GetAsync();

        Assert.Equal("Read the generated code", todo!.Title);
    }

#if (codeFirst && throwsMode)
    /// <summary>
    /// 200, not 201, and the point of the assertion is the mode: throws mode names one success type
    /// per handler and has nowhere to put a status beside it. What the client does not surface, the
    /// transport does.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersTwoHundred(TemplateModuleNameClient client) {
        var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

        Assert.Equal("ship it", todo!.Title);
        Assert.Equal(200, LastResponse.Status);
    }
#else
    /// <summary>
    /// What the client does not surface, the transport does: the status it did not throw on and the
    /// headers that came with it.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TemplateModuleNameClient client) {
        var todo = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" });

        Assert.Equal("ship it", todo!.Title);
        Assert.Equal(201, LastResponse.Status);
#if (!smithy)
        Assert.Equal($"/todos/{todo.Id}", LastResponse.Headers["Location"]);
#endif
    }
#endif

#if (codeFirst && throwsMode)
    /// <summary>
    /// Throws mode documents only the 200, so the generated client has no 404 branch: the same
    /// request that answers a typed Problem under the response model is a bare ApiException here.
    /// The declared models close exactly that gap.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsAnUntypedFailure(TemplateModuleNameClient client) {
        var failure = await Assert.ThrowsAsync<ApiException>(() => client.Todos[9999].GetAsync());

        Assert.Equal(404, failure.ResponseStatusCode);
    }
#endif
#if (codeFirst && declaredMode)
    /// <summary>
    /// The 404 is in the signature, so it is in the document, so Kiota generated a typed exception
    /// for it - named after the case, NotFound, and carrying the body the server answered.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedNotFound(TemplateModuleNameClient client) {
        var missing = await Assert.ThrowsAsync<ClientModels.NotFound>(() => client.Todos[9999].GetAsync());

        Assert.Equal(404, missing.ResponseStatusCode);
        Assert.Contains("9999", missing.Detail);
    }
#endif
#if (openapi)
    /// <summary>
    /// The contract declares the 404 with its Problem body, whichever response model the service
    /// implements it in, so the client throws the typed exception either way.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedProblem(TemplateModuleNameClient client) {
        var problem = await Assert.ThrowsAsync<ClientModels.Problem>(() => client.Todos[9999].GetAsync());

        Assert.Equal(404, problem.ResponseStatusCode);
        Assert.Contains("9999", problem.Detail);
    }
#endif
#if (smithy)
    /// <summary>
    /// A Smithy error is a named shape, so the typed exception is named for it rather than for a
    /// shared Problem schema.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedError(TemplateModuleNameClient client) {
        var failure = await Assert.ThrowsAsync<ClientModels.TodoNotFound>(() => client.Todos[9999].GetAsync());

        Assert.Equal(404, failure.ResponseStatusCode);
    }
#endif

    [HardenedTest]
    public async Task RemoveTodo_ThroughTheGeneratedClient(TemplateModuleNameClient client) {
        await client.Todos[2].DeleteAsync();

#if (codeFirst && throwsMode)
        Assert.Equal(200, LastResponse.Status);
#else
        Assert.Equal(204, LastResponse.Status);
#endif
    }
}
