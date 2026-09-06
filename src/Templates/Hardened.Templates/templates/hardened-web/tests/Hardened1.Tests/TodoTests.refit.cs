namespace Hardened1.Tests;

/// <summary>
/// Every operation, driven through the generated client with no socket.
/// </summary>
/// <remarks>
/// The interface is a test parameter; [assembly: RefitTesting] in Bootstrap.cs is what builds it,
/// over the same in-process chain ITestWebApp drives - routing, filters, binding, the handler and
/// serialisation. Every operation returns an IApiResponse - set in src/Hardened1.Client/.refitter -
/// so a call is asserted with Returns&lt;T&gt;(), naming the response type the contract declares:
/// the status, the body type and the headers that status carries in one word, and nothing throws.
/// ReturnsStatus&lt;T&gt;() asserts the status alone, for one the document declares no body for.
///
/// Every declared status is asserted, not only the happy one. A response model that is only ever
/// exercised at 200 is indistinguishable from one that has no declared set at all, which is the
/// thing these tests exist to tell apart.
/// </remarks>
public class TodoTests {

    [HardenedTest]
    public async Task ListTodos_ReturnsEveryTodo(ITemplateModuleNameClient client) {
        var todos = await client.ListTodos().Returns<Ok<ICollection<ClientModels.Todo>>>();

#if (xunit)
        Assert.Equal([1, 2], todos.Value.Select(todo => todo.Id));
#else
        Assert.That(todos.Value.Select(todo => todo.Id), Is.EqualTo(new[] { 1, 2 }));
#endif
    }

    [HardenedTest]
    public async Task GetTodo_ReturnsTheTodo(ITemplateModuleNameClient client) {
        var todo = await client.GetTodo(1).Returns<Ok<ClientModels.Todo>>();

#if (xunit)
        Assert.Equal("Read the generated code", todo.Value.Title);
#else
        Assert.That(todo.Value.Title, Is.EqualTo("Read the generated code"));
#endif
    }

#if (codeFirst)
#if (throwsMode)
    /// <summary>
    /// [Throws&lt;NotFound&gt;] on the handler puts the 404 in the document, so Refitter generated a
    /// model for its body - named after the case, NotFound - and the refusal is read as it, through
    /// the client's own serializer. Without the attribute the status is all the client can see.
    /// </summary>
#else
    /// <summary>
    /// The 404 is in the signature, so it is in the document, so Refitter generated a model for its
    /// body - named after the case, NotFound - and the refusal is read as it, through the client's
    /// own serializer.
    /// </summary>
#endif
    [HardenedTest]
    public async Task GetTodo_UnknownId_IsATypedNotFound(ITemplateModuleNameClient client) {
        var missing = await client.GetTodo(9999).Returns<NotFound<ClientModels.NotFound>>();

#if (xunit)
        Assert.Contains("9999", missing.Body.Detail);
#else
        Assert.That(missing.Body.Detail, Does.Contain("9999"));
#endif
    }

    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsATypedNotFound(ITemplateModuleNameClient client) {
        var missing = await client.RemoveTodo(9999).Returns<NotFound<ClientModels.NotFound>>();

#if (xunit)
        Assert.Contains("9999", missing.Body.Detail);
#else
        Assert.That(missing.Body.Detail, Does.Contain("9999"));
#endif
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409 - typed, like the 404.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsATypedConflict(ITemplateModuleNameClient client) {
        var taken = await client.CreateTodo(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .Returns<Conflict<ClientModels.Conflict>>();

#if (xunit)
        Assert.Contains("Add an endpoint", taken.Body.Detail);
#else
        Assert.That(taken.Body.Detail, Does.Contain("Add an endpoint"));
#endif
    }
#endif
#if (openapi)
    /// <summary>
    /// The contract declares the 404 with its Problem body, whichever response model the service
    /// implements it in, so the refusal is read as that model either way.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_UnknownId_IsATypedProblem(ITemplateModuleNameClient client) {
#if (declaredMode)
        var missing = await client.GetTodo(9999).Returns<NotFound<ClientModels.Problem>>();

        // The declared case carries the detail the service wrote. Throws mode answers this 404 by
        // returning null, which is the document's body with nothing in it to say why.
#if (xunit)
        Assert.Contains("9999", missing.Body.Detail);
#else
        Assert.That(missing.Body.Detail, Does.Contain("9999"));
#endif
#else
        await client.GetTodo(9999).Returns<NotFound<ClientModels.Problem>>();
#endif
    }

    /// <summary>The remove says why in every mode, because it throws or returns a case rather than null.</summary>
    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsATypedProblem(ITemplateModuleNameClient client) {
        var missing = await client.RemoveTodo(9999).Returns<NotFound<ClientModels.Problem>>();

#if (xunit)
        Assert.Contains("9999", missing.Body.Detail);
#else
        Assert.That(missing.Body.Detail, Does.Contain("9999"));
#endif
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409, carrying the same Problem.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsATypedProblem(ITemplateModuleNameClient client) {
        var taken = await client.CreateTodo(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .Returns<Conflict<ClientModels.Problem>>();

#if (xunit)
        Assert.Contains("Add an endpoint", taken.Body.Detail);
#else
        Assert.That(taken.Body.Detail, Does.Contain("Add an endpoint"));
#endif
    }
#endif
#if (smithy)
    /// <summary>
    /// A Smithy error is a named shape, so the model is named for it rather than for a shared
    /// Problem schema.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_UnknownId_IsATypedError(ITemplateModuleNameClient client) {
        await client.GetTodo(9999).Returns<NotFound<ClientModels.TodoNotFound>>();
    }

    /// <summary>The remove says why in every mode, because it throws or returns a case rather than null.</summary>
    [HardenedTest]
    public async Task RemoveTodo_UnknownId_IsATypedError(ITemplateModuleNameClient client) {
        var missing = await client.RemoveTodo(9999).Returns<NotFound<ClientModels.TodoNotFound>>();

#if (xunit)
        Assert.Contains("9999", missing.Body.Message);
#else
        Assert.That(missing.Body.Message, Does.Contain("9999"));
#endif
    }

    /// <summary>Titles are unique, which is what gives the sample a real 409, as the shape the model names for it.</summary>
    [HardenedTest]
    public async Task CreateTodo_DuplicateTitle_IsATypedError(ITemplateModuleNameClient client) {
        var taken = await client.CreateTodo(new ClientModels.NewTodo { Title = "Add an endpoint" })
            .Returns<Conflict<ClientModels.TodoTitleTaken>>();

#if (xunit)
        Assert.Contains("Add an endpoint", taken.Body.Message);
#else
        Assert.That(taken.Body.Message, Does.Contain("Add an endpoint"));
#endif
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
    public async Task CreateTodo_AnswersTwoHundred(ITemplateModuleNameClient client) {
        var answer = await client.CreateTodo(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Ok<ClientModels.Todo>>();

#if (xunit)
        Assert.Equal("ship it", answer.Value.Title);
#else
        Assert.That(answer.Value.Title, Is.EqualTo("ship it"));
#endif
    }

    /// <summary>200 with the removed todo, for the same reason.</summary>
    [HardenedTest]
    public async Task RemoveTodo_AnswersTwoHundred(ITemplateModuleNameClient client) {
        var removed = await client.RemoveTodo(2).Returns<Ok<ClientModels.Todo>>();

#if (xunit)
        Assert.Equal(2, removed.Value.Id);
#else
        Assert.That(removed.Value.Id, Is.EqualTo(2));
#endif
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
    public async Task CreateTodo_AnswersCreatedWithALocation(ITemplateModuleNameClient client) {
        var created = await client.CreateTodo(new ClientModels.NewTodo { Title = "ship it" })
            .Returns<Created<ClientModels.Todo>>();

#if (xunit)
        Assert.Equal("ship it", created.Value.Title);
        Assert.Equal($"/todos/{created.Value.Id}", created.Location);
#else
        Assert.That(created.Value.Title, Is.EqualTo("ship it"));
        Assert.That(created.Location, Is.EqualTo($"/todos/{created.Value.Id}"));
#endif
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
    public async Task RemoveTodo_AnswersNoContent(ITemplateModuleNameClient client) {
        await client.RemoveTodo(2).Returns<NoContent>();
    }
#endif

    /// <summary>
    /// The constraints on the request are enforced before the handler runs.
    /// </summary>
    /// <remarks>
    /// Nothing in this project validates anything by hand. The title's length limit became a filter
    /// in front of the handler, so a value too long never reaches the code - and the published
    /// document declares the 400 it answers with, so the client has a model for it that names the
    /// field.
    /// </remarks>
    [HardenedTest]
    public async Task CreateTodo_TitleOverItsLimit_IsBadRequest(ITemplateModuleNameClient client) {
        var refused = await client.CreateTodo(new ClientModels.NewTodo { Title = new string('x', 100) })
            .Returns<BadRequest<ClientModels.RequestValidationError>>();

#if (xunit)
        Assert.Contains(
            refused.Body.Errors,
            error => error.Field.Contains("title", StringComparison.OrdinalIgnoreCase));
#else
        Assert.That(refused.Body.Errors.Select(error => error.Field), Has.Some.Contains("title").IgnoreCase);
#endif
    }

    /// <summary>
    /// The id's minimum is enforced the same way the title's length is - and the published
    /// document says so, which DocumentStatusTests holds it to.
    /// </summary>
    [HardenedTest]
    public async Task GetTodo_IdBelowItsMinimum_IsBadRequest(ITemplateModuleNameClient client) {
        await client.GetTodo(0).Returns<BadRequest<ClientModels.RequestValidationError>>();
    }

    [HardenedTest]
    public async Task RemoveTodo_IdBelowItsMinimum_IsBadRequest(ITemplateModuleNameClient client) {
        await client.RemoveTodo(0).Returns<BadRequest<ClientModels.RequestValidationError>>();
    }
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
}
