namespace Hardened1.Tests;

/// <summary>
/// The generated client, driven through the real pipeline with no socket.
/// </summary>
/// <remarks>
/// The interface is a test parameter; [assembly: RefitTesting] in Bootstrap.cs is what builds it.
/// Every operation returns an IApiResponse - set in src/Hardened1.Client/.refitter - so a call is
/// asserted with Returns&lt;T&gt;(), naming the response type the contract declares: the status,
/// the body type and the headers that status carries in one word, and nothing throws.
/// </remarks>
public class TemplateModuleNameClientTests {

    [HardenedTest]
    public async Task ListTodos_ThroughTheGeneratedClient(ITemplateModuleNameClient client) {
#if (codeFirst)
        var todos = await client.All().Returns<Ok<ICollection<ClientModels.Todo>>>();
#else
        var todos = await client.ListTodos().Returns<Ok<ICollection<ClientModels.Todo>>>();
#endif

        Assert.Equal([1, 2], todos.Value.Select(todo => todo.Id));
    }

    [HardenedTest]
    public async Task GetTodo_ThroughTheGeneratedClient(ITemplateModuleNameClient client) {
#if (codeFirst)
        var todo = await client.ById(1).Returns<Ok<ClientModels.Todo>>();
#else
        var todo = await client.GetTodo(1).Returns<Ok<ClientModels.Todo>>();
#endif

        Assert.Equal("Read the generated code", todo.Value.Title);
    }

#if (codeFirst && throwsMode)
    /// <summary>
    /// 200, not 201, and the point of the assertion is the mode: throws mode names one success type
    /// per handler and has nowhere to put a status beside it.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersTwoHundred(ITemplateModuleNameClient client) {
        var answer = await client.Create(new ClientModels.NewTodo { Title = "ship it" })
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
    public async Task CreateTodo_AnswersCreated(ITemplateModuleNameClient client) {
        await client.CreateTodo(new ClientModels.NewTodo { Title = "ship it" })
            .ReturnsStatus<Created<ClientModels.Todo>>();
    }
#else
    /// <summary>
    /// 201 and a Location header, both declared in the response set, and both on the envelope
    /// Refit hands back - which is where Created reads them from.
    /// </summary>
    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(ITemplateModuleNameClient client) {
#if (codeFirst)
        var created = await client.Create(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();
#else
        var created = await client.CreateTodo(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();
#endif

        Assert.Equal("ship it", created.Value.Title);
        Assert.Equal($"/todos/{created.Value.Id}", created.Location);
    }
#endif
#endif

#if (codeFirst && throwsMode)
    /// <summary>
    /// Throws mode documents only the 200, so the document declares no body for the 404 and there
    /// is no type to name: the status is what is asserted. The declared models close exactly that gap.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsAnUntypedFailure(ITemplateModuleNameClient client) {
        await client.ById(9999).ReturnsStatus<NotFound>();
    }
#endif
#if (codeFirst && declaredMode)
    /// <summary>
    /// The 404 is in the signature, so it is in the document, so Refitter generated a model for its
    /// body - named after the case, NotFound - and the refusal is read as it, through the client's
    /// own serializer.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedNotFound(ITemplateModuleNameClient client) {
        var missing = await client.ById(9999).Returns<NotFound<ClientModels.NotFound>>();

        Assert.Contains("9999", missing.Body.Detail);
    }
#endif
#if (openapi)
    /// <summary>
    /// The contract declares the 404 with its Problem body, whichever response model the service
    /// implements it in, so the refusal is read as that model either way.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedProblem(ITemplateModuleNameClient client) {
#if (declaredMode)
        var missing = await client.GetTodo(9999).Returns<NotFound<ClientModels.Problem>>();

        // The declared case carries the detail the service wrote. Throws mode answers this 404 by
        // returning null, which is the document's body with nothing in it to say why.
        Assert.Contains("9999", missing.Body.Detail);
#else
        await client.GetTodo(9999).Returns<NotFound<ClientModels.Problem>>();
#endif
    }
#endif
#if (smithy)
    /// <summary>
    /// A Smithy error is a named shape, so the model is named for it rather than for a shared
    /// Problem schema.
    /// </summary>
    [HardenedTest]
    public async Task UnknownTodo_IsATypedError(ITemplateModuleNameClient client) {
        await client.GetTodo(9999).Returns<NotFound<ClientModels.TodoNotFound>>();
    }
#endif

#if (codeFirst && throwsMode)
    /// <summary>200 with the removed todo, for the same reason the create answers 200.</summary>
    [HardenedTest]
    public async Task RemoveTodo_ThroughTheGeneratedClient(ITemplateModuleNameClient client) {
        var removed = await client.Remove(2).Returns<Ok<ClientModels.Todo>>();

        Assert.Equal(2, removed.Value.Id);
    }
#else
    [HardenedTest]
    public async Task RemoveTodo_ThroughTheGeneratedClient(ITemplateModuleNameClient client) {
#if (codeFirst)
        await client.Remove(2).Returns<NoContent>();
#else
        await client.RemoveTodo(2).Returns<NoContent>();
#endif
    }
#endif
}
