namespace Hardened1.Tests;

/// <summary>
/// Every operation, driven through the generated client with no socket.
/// </summary>
/// <remarks>
/// The client is a test parameter; [assembly: KiotaTesting] in Bootstrap.cs is what builds it, over
/// the same in-process chain ITestWebApp drives - routing, filters, binding, the handler and
/// serialisation. A call is asserted with Returns&lt;T&gt;(), naming the response type the contract
/// declares - Created&lt;Todo&gt;, NotFound&lt;Problem&gt;, NoContent - which is the status, the
/// body type and the headers that status carries in one word, for a success the client returns and
/// a refusal it throws alike. ReturnsStatus&lt;T&gt;() asserts the status alone, for one the
/// document declares no body for.
///
/// Every declared status is asserted, not only the happy one. A response model that is only ever
/// exercised at 200 is indistinguishable from one that has no declared set at all, which is the
/// thing these tests exist to tell apart.
/// </remarks>
public class TodoTests {

    [HardenedTest]
    public async Task ListTodos_ReturnsEveryTodo(TemplateModuleNameClient client) {
        var todos = await client.Todos.GetAsync().Returns<Ok<List<ClientModels.Todo>>>();

        Assert.Equal([1, 2], todos.Value.Select(todo => todo.Id!.Value));
    }

    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(TemplateModuleNameClient client) {
        var todo = await client.Todos[1].GetAsync().Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("Read the generated code", todo.Value.Title);
    }

#if (codeFirst && throwsMode)
    /// <summary>
    /// Throws mode documents only the 200, so the generated client has no 404 branch: the same
    /// request that answers a typed NotFound under the response model is a bare ApiException here,
    /// with no body type to name. The status is what is asserted, and the declared models close
    /// exactly that gap.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_UnknownId_IsAnUntypedNotFound(TemplateModuleNameClient client) {
        await client.Todos[9999].GetAsync().ReturnsStatus<NotFound>();
    }

    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsAnUntypedNotFound(TemplateModuleNameClient client) {
        await client.Todos[9999].DeleteAsync().ReturnsStatus<NotFound>();
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409 - thrown and undocumented, like the 404.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsAnUntypedConflict(TemplateModuleNameClient client) {
        await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .ReturnsStatus<Conflict>();
    }
#endif
#if (codeFirst && declaredMode)
    /// <summary>
    /// The 404 is in the signature, so it is in the document, so Kiota generated a typed exception
    /// for it - named after the case, NotFound, and carrying the body the server answered.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_UnknownId_IsATypedNotFound(TemplateModuleNameClient client) {
        var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.NotFound>>();

        Assert.Contains("9999", missing.Body.Detail);
    }

    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsATypedNotFound(TemplateModuleNameClient client) {
        var missing = await client.Todos[9999].DeleteAsync().Returns<NotFound<ClientModels.NotFound>>();

        Assert.Contains("9999", missing.Body.Detail);
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409 - typed, like the 404.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsATypedConflict(TemplateModuleNameClient client) {
        var taken = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .Returns<Conflict<ClientModels.Conflict>>();

        Assert.Contains("Add an endpoint", taken.Body.Detail);
    }
#endif
#if (openapi)
    /// <summary>
    /// The contract declares the 404 with its Problem body, whichever response model the service
    /// implements it in, so the client throws the typed exception either way.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_UnknownId_IsATypedProblem(TemplateModuleNameClient client) {
#if (declaredMode)
        var missing = await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.Problem>>();

        // The declared case carries the detail the service wrote. Throws mode answers this 404 by
        // returning null, which is the document's body with nothing in it to say why.
        Assert.Contains("9999", missing.Body.Detail);
#else
        await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.Problem>>();
#endif
    }

    /// <summary>The remove says why in every mode, because it throws or returns a case rather than null.</summary>
    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsATypedProblem(TemplateModuleNameClient client) {
        var missing = await client.Todos[9999].DeleteAsync().Returns<NotFound<ClientModels.Problem>>();

        Assert.Contains("9999", missing.Body.Detail);
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409, carrying the same Problem.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsATypedProblem(TemplateModuleNameClient client) {
        var taken = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .Returns<Conflict<ClientModels.Problem>>();

        Assert.Contains("Add an endpoint", taken.Body.Detail);
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
    public async Task GetTodo_UnknownId_IsATypedError(TemplateModuleNameClient client) {
        await client.Todos[9999].GetAsync().Returns<NotFound<ClientModels.TodoNotFound>>();
    }

    /// <summary>The remove says why in every mode, because it throws or returns a case rather than null.</summary>
    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsATypedError(TemplateModuleNameClient client) {
        var missing = await client.Todos[9999].DeleteAsync().Returns<NotFound<ClientModels.TodoNotFound>>();

        Assert.Contains("9999", missing.Body.Message);
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409, as the shape the model names for it.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsATypedError(TemplateModuleNameClient client) {
        var taken = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .Returns<Conflict<ClientModels.TodoTitleTaken>>();

        Assert.Contains("Add an endpoint", taken.Body.Message);
    }
#endif

#if (codeFirst && throwsMode)
    /// <summary>
    /// 200, not 201 - and that is the point of the assertion rather than an oversight.
    /// </summary>
    /// <remarks>
    /// Throws mode names one success type per handler and has no way to put a status beside it,
    /// so a created todo comes back at 200. Generate this template with --response-model response
    /// and the same route answers 201 with a Location header, because the status moved into the
    /// signature. This test is what makes that difference visible rather than a claim in a comment.
    /// </remarks>
    [HardenedTest]
    public async Task CreateTodo_AnswersTwoHundred(TemplateModuleNameClient client) {
        var answer = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Ok<ClientModels.Todo>>();

        Assert.Equal("ship it", answer.Value.Title);
    }

    /// <summary>200 with the removed todo, for the same reason.</summary>
    [HardenedTest]
    public async Task RemoveTodo_AnswersTwoHundred(TemplateModuleNameClient client) {
        var removed = await client.Todos[2].DeleteAsync().Returns<Ok<ClientModels.Todo>>();

        Assert.Equal(2, removed.Value.Id);
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
    public async Task CreateTodo_AnswersCreatedWithALocation(TemplateModuleNameClient client) {
        var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();

        Assert.Equal("ship it", created.Value.Title);
        Assert.Equal($"/todos/{created.Value.Id}", created.Location);
    }
#endif

    /// <summary>
    /// 204, and no body with it.
    /// </summary>
    /// <remarks>
    /// The case carries nothing, and the generated dispatch is told not to serialise one - so this
    /// is an empty body rather than the four characters "null". NoContent is the expectation that
    /// says so.
    /// </remarks>
    [HardenedTest]
    public async Task RemoveTodo_AnswersNoContent(TemplateModuleNameClient client) {
        await client.Todos[2].DeleteAsync().Returns<NoContent>();
    }
#endif

#if (specFirst)
    /// <summary>
    /// The constraints in the contract are enforced before the handler runs.
    /// </summary>
    /// <remarks>
    /// Nothing in this project validates anything. maxLength on the title became a filter in front
    /// of the generated handler, so a value too long never reaches the code - and the published
    /// document declares the 400 it answers with, so the client has a typed branch for it that
    /// names the field.
    /// </remarks>
    [HardenedTest]
    public async Task CreateTodo_TitleTheContractDisallows_IsBadRequest(TemplateModuleNameClient client) {
        var refused = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = new string('x', 100) })
            .Returns<BadRequest<ClientModels.RequestValidationError>>();

        Assert.Contains(
            refused.Body.Errors!,
            error => error.Field?.Contains("title", StringComparison.OrdinalIgnoreCase) == true);
    }

    /// <summary>
    /// The id's minimum is enforced the same way the title's length is - and the published
    /// document says so, which DocumentStatusTests holds it to.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_IdBelowTheContractsMinimum_IsBadRequest(TemplateModuleNameClient client) {
        await client.Todos[0].GetAsync().Returns<BadRequest<ClientModels.RequestValidationError>>();
    }

    [HardenedTest]
    public async Task RemoveTodo_IdBelowTheContractsMinimum_IsBadRequest(TemplateModuleNameClient client) {
        await client.Todos[0].DeleteAsync().Returns<BadRequest<ClientModels.RequestValidationError>>();
    }
#endif
#if (codeFirst)
    /// <summary>
    /// An id the parameter's type cannot carry is refused before the handler, with the same
    /// field-level envelope a failed validation answers - and the published document says so,
    /// which DocumentStatusTests holds it to.
    /// </summary>
    /// <remarks>
    /// The one request here the generated client cannot make: its path parameter is an int, which
    /// is the point. ITestWebApp sends the raw request through the same pipeline.
    /// </remarks>
    [HardenedTest]
    public async Task GetTodo_MalformedId_IsBadRequest(ITestWebApp app) {
        (await app.Get("/todos/not-a-number")).Assert.BadRequest();
    }

    [HardenedTest]
    public async Task RemoveTodo_MalformedId_IsBadRequest(ITestWebApp app) {
        (await app.Delete("/todos/not-a-number")).Assert.BadRequest();
    }
#endif
}
