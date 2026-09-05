namespace Hardened1.Tests;

/// <summary>
/// The generated client, driven through the real pipeline with no socket.
/// </summary>
/// <remarks>
/// The client is a test parameter; [assembly: KiotaTesting] in Bootstrap.cs is what builds it. A
/// call is asserted with Returns&lt;T&gt;(), naming the response type the contract declares -
/// Created&lt;Todo&gt;, NotFound&lt;Problem&gt;, NoContent - which is the status, the body type
/// and the headers that status carries in one word, for a success the client returns and a
/// refusal it throws alike.
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
    /// per handler and has nowhere to put a status beside it.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersTwoHundred(TemplateModuleNameClient client) {
        var answer = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("ship it", answer.Value.Title);
    }
#else
#if (smithy)
    /// <summary>
    /// 201, as the operation declares. The Smithy contract puts no Location on it, so the status
    /// is what is asserted.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TemplateModuleNameClient client) {
        await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .ReturnsStatus<Created<ClientModels.Todo>>();
    }
#else
    /// <summary>
    /// 201 and a Location header, both declared in the response set. The client returns the todo
    /// alone; the status it did not throw on and the header that came with it are read from what
    /// the client received, and Created carries all three.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TemplateModuleNameClient client) {
        var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();

        Assert.Equal("ship it", created.Value.Title);
        Assert.Equal($"/todos/{created.Value.Id}", created.Location);
    }
#endif
#endif

#if (codeFirst && throwsMode)
    /// <summary>
    /// Throws mode documents only the 200, so the generated client has no 404 branch: the same
    /// request that answers a typed NotFound under the response model is a bare ApiException here,
    /// with no body type to name. The declared models close exactly that gap.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsAnUntypedFailure(TemplateModuleNameClient client) {
        await client.Todos[9999].GetAsync().ReturnsStatus<NotFound>();
    }
#endif
#if (codeFirst && declaredMode)
    /// <summary>
    /// The 404 is in the signature, so it is in the document, so Kiota generated a typed exception
    /// for it - named after the case, NotFound, and carrying the body the server answered.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedNotFound(TemplateModuleNameClient client) {
        var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.NotFound>>();

        Assert.Contains("9999", missing.Body.Detail);
    }
#endif
#if (openapi)
    /// <summary>
    /// The contract declares the 404 with its Problem body, whichever response model the service
    /// implements it in, so the client throws the typed exception either way.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedProblem(TemplateModuleNameClient client) {
#if (declaredMode)
        var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.Problem>>();

        // The declared case carries the detail the service wrote. Throws mode answers this 404 by
        // returning null, which is the document's body with nothing in it to say why.
        Assert.Contains("9999", missing.Body.Detail);
#else
        await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.Problem>>();
#endif
    }
#endif
#if (smithy)
    /// <summary>
    /// A Smithy error is a named shape, so the typed exception is named for it rather than for a
    /// shared Problem schema.
    /// </summary>
#if (throwsMode)
    /// <remarks>
    /// In throws mode this service answers the 404 by returning null, and the message is the
    /// status's reason phrase: Smithy gives an @error's message one meaning, so the runtime fills
    /// it rather than sending the bodiless 404 that used to make the client throw a bare
    /// ApiException. A handler with something to say throws
    /// new TodoNotFound("...").AsException() instead.
    /// </remarks>
#endif
    [HardenedTest]
    public async Task UnknownTodo_IsATypedError(TemplateModuleNameClient client) {
        await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.TodoNotFound>>();
    }
#endif

#if (codeFirst && throwsMode)
    /// <summary>200 with the removed todo, for the same reason the create answers 200.</summary>
    [HardenedTest]
    public async Task RemoveTodo_ThroughTheGeneratedClient(TemplateModuleNameClient client) {
        var removed = await client.Todos[2].DeleteAsync().Returns<Ok<ClientModels.Todo>>();

        Assert.Equal(2, removed.Value.Id);
    }
#else
    [HardenedTest]
    public async Task RemoveTodo_ThroughTheGeneratedClient(TemplateModuleNameClient client) {
        await client.Todos[2].DeleteAsync().Returns<NoContent>();
    }
#endif
}
