using Hardened.Requests.Abstract.Responses;
using Hardened.Web.Runtime.Attributes;

namespace Hardened1;

#if (unionMode)
/// <summary>
/// What each operation can answer with.
/// </summary>
/// <remarks>
/// A C# 15 union. One case per outcome, and the compiler makes a caller handle all of them - which
/// is the whole difference between this and the Response mode: the same declared set, plus
/// exhaustiveness where you match on it.
///
/// Per-status wrapper types rather than two bare payloads. Two identical case types in one union
/// give two identical conversions and the compiler rejects the use site, so NotFound appearing in
/// two different unions is fine and appearing twice in one is not.
/// </remarks>
public union TodoResult(Todo, NotFound);

public union NewTodoResult(Created<Todo>, Conflict);

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

#if (standardMode)
    /// <summary>One todo, or 404.</summary>
    /// <remarks>
    /// Standard mode: the signature names the success type, and every other status is thrown. The
    /// thrown value is the same NotFound record the declared modes return, so the 404 body is
    /// identical either way - what differs is whether the compiler knows the route can answer it.
    /// </remarks>
    [Get("/{id}")]
    public Todo ById(TodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null) {
            throw new NotFound("todo", $"No todo has id {id}.").AsException();
        }

        return todo;
    }

    /// <summary>
    /// Creates one, or 409 when the title is taken.
    /// </summary>
    /// <remarks>
    /// This answers 200 rather than 201. A handler in this mode has one success type and no way to
    /// name a status beside it - Created&lt;Todo&gt; would be serialised as an ordinary body at 200,
    /// because nothing in the standard dispatch reads IHttpStatusResponse off a returned value.
    /// The declared modes are where 201 becomes expressible.
    /// </remarks>
    [Post("/")]
    public Todo Create(TodoStore store, NewTodo request) {
        if (store.TitleExists(request.Title)) {
            throw new Conflict($"A todo titled '{request.Title}' already exists.").AsException();
        }

        return store.Add(request.Title);
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
    public Response<Todo, NotFound> ById(TodoStore store, int id) {
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
    public Response<Created<Todo>, Conflict> Create(TodoStore store, NewTodo request) {
        if (store.TitleExists(request.Title)) {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
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
    public TodoResult ById(TodoStore store, int id) {
        var todo = store.Find(id);

        if (todo is null) {
            return new NotFound("todo", $"No todo has id {id}.");
        }

        return todo;
    }

    /// <summary>Creates one at 201 with a Location header, or 409 when the title is taken.</summary>
    [Post("/")]
    public NewTodoResult Create(TodoStore store, NewTodo request) {
        if (store.TitleExists(request.Title)) {
            return new Conflict($"A todo titled '{request.Title}' already exists.");
        }

        var todo = store.Add(request.Title);

        return new Created<Todo>(todo, $"/todos/{todo.Id}");
    }

#endif
}
