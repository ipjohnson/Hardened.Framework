using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;

namespace Hardened1;

#if (unionMode)
/// <summary>
/// What each operation can answer with.
/// </summary>
/// <remarks>
/// C# 15 unions. One case per outcome, and the compiler makes a caller handle all of them - which
/// is the whole difference from the Response mode: the same declared set, plus exhaustiveness
/// wherever you match on it.
///
/// Per-status wrapper types rather than two bare payloads. Two identical case types in one union
/// give two identical conversions and the compiler rejects the use site, so NotFound appearing in
/// two different unions is fine and appearing twice in one is not.
/// </remarks>
public union TodoResult(Todo, NotFound);

public union NewTodoResult(Created<Todo>, Conflict);

public union RemovedTodoResult(NoContent, NotFound);

#endif
/// <summary>
/// A plain class. No base type, no interface, no registration - the generator finds the route
/// attribute at build time and emits a handler bound to this method's exact signature.
/// </summary>
/// <remarks>
/// Services arrive as method parameters, alongside the route and body values. Anything the
/// container knows about can be asked for here, and nothing has to be stored on the class.
/// </remarks>
public class TodoController {

    /// <summary>Every todo.</summary>
    /// <remarks>
    /// The one route here with a single outcome, so it reads the same under every response model -
    /// there is nothing to declare beside the success type. The collection becomes an array in the
    /// generated document without anything here describing it.
    /// </remarks>
    [Get("/")]
    public IReadOnlyList<Todo> All(ITodoStore store) => store.All();

#if (standardMode)
    /// <summary>One todo, or 404.</summary>
    /// <remarks>
    /// Standard mode: the signature names the success type, and every other status is thrown. The
    /// thrown value is the same NotFound record the declared modes return, so the 404 body is
    /// identical either way - what differs is whether the compiler knows the route can answer it.
    /// </remarks>
    [Get("/{id}")]
    public Todo ById(ITodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null) {
            throw new NotFound("todo", $"No todo has id {id}.").AsException();
        }

        return todo;
    }

    /// <summary>Creates one, or 409 when the title is taken.</summary>
    /// <remarks>
    /// This answers 200 rather than 201. A handler in this mode has one success type and no way to
    /// name a status beside it - returning Created&lt;Todo&gt; would serialise it as an ordinary
    /// body at 200, because the standard dispatch does not read IHttpStatusResponse off a returned
    /// value. Compare the same method under --response-model response, where 201 is in the type.
    /// </remarks>
    [Post("/")]
    public Todo Create(ITodoStore store, NewTodo request) {
        if (store.TitleExists(request.Title)) {
            throw new Conflict($"A todo titled '{request.Title}' already exists.").AsException();
        }

        return store.Add(request.Title);
    }

    /// <summary>Removes one, or 404. Answers 200 with the removed todo, for the reason above.</summary>
    [Delete("/{id}")]
    public Todo Remove(ITodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null || !store.Remove(id)) {
            throw new NotFound("todo", $"No todo has id {id}.").AsException();
        }

        return todo;
    }
#endif
#if (responseMode)
    /// <summary>One todo, or 404.</summary>
    /// <remarks>
    /// The return type is the whole declared set. Response&lt;T1..Tn&gt; is an ordinary struct with
    /// one implicit conversion per case, so a handler returns the payload and never names the
    /// wrapper - and the generated document describes both statuses, because both are in the
    /// signature rather than in a throw somewhere down the call stack.
    /// </remarks>
    [Get("/{id}")]
    public Response<Todo, NotFound> ById(ITodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }

    /// <summary>Creates one at 201 with a Location header, or 409 when the title is taken.</summary>
    /// <remarks>
    /// Created&lt;T&gt; carries the body and the Location header together, and the status comes from
    /// the case rather than from anything this method does.
    /// </remarks>
    [Post("/")]
    public Response<Created<Todo>, Conflict> Create(ITodoStore store, NewTodo request) {
        if (store.TitleExists(request.Title)) {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }

    /// <summary>Removes one at 204, or 404.</summary>
    /// <remarks>
    /// NoContent carries no body and the generated dispatch knows not to serialise one, so this
    /// answers 204 with an empty body rather than 200 with "null" in it.
    /// </remarks>
    [Delete("/{id}")]
    public Response<NoContent, NotFound> Remove(ITodoStore store, int id) {
        if (store.Find(id) is null || !store.Remove(id)) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return new NoContent();
    }
#endif
#if (unionMode)
    /// <summary>One todo, or 404.</summary>
    /// <remarks>
    /// Identical to the Response mode apart from the return type. The generator matches both
    /// structurally - a public single-parameter constructor per case and a public object? Value -
    /// so moving between the two rewrites no handler body and changes no generated dispatch.
    /// </remarks>
    [Get("/{id}")]
    public TodoResult ById(ITodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }

    /// <summary>Creates one at 201 with a Location header, or 409 when the title is taken.</summary>
    [Post("/")]
    public NewTodoResult Create(ITodoStore store, NewTodo request) {
        if (store.TitleExists(request.Title)) {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }

    /// <summary>Removes one at 204, or 404.</summary>
    [Delete("/{id}")]
    public RemovedTodoResult Remove(ITodoStore store, int id) {
        if (store.Find(id) is null || !store.Remove(id)) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return new NoContent();
    }
#endif
}
