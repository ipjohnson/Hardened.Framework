using DependencyModules.Runtime.Attributes;
#if (specFirst)
using Hardened1.Models;
#endif
#if (codeFirst)
using ValidationModules.Constraints;
#if (messagePack)
using MessagePack;
#endif
#endif

namespace Hardened1;

#if (codeFirst)
#if (messagePackKeyed)
/// <summary>A todo, as it goes over the wire.</summary>
/// <remarks>
/// [MessagePackObject] and partial are what MessagePack's source generator needs to write a
/// formatter for this type, and [Key(n)] is what identifies each member on that wire. You write
/// the indices: Hardened cannot add an attribute to a member of a type it did not declare, and an
/// index it invented for the document alone would describe a wire format this service does not
/// speak. What it does do is publish them - each one reaches the document as
/// x-message-pack-index, and the generated client reads them from there.
///
/// The numbers are the contract. Renaming Title is free; renumbering it breaks every client
/// generated before the change, and nothing in a document diff makes that obvious.
/// </remarks>
[MessagePackObject]
public partial record Todo(
    [property: Key(0)] int Id,
    [property: Key(1)] string Title,
    [property: Key(2)] bool Done);
#endif
#if (messagePackNamed)
/// <summary>A todo, as it goes over the wire.</summary>
/// <remarks>
/// [MessagePackObject(true)] and partial are what MessagePack's source generator needs to write a
/// formatter for this type. The true is keyAsPropertyName: each member is identified on the wire
/// by its name rather than by an integer.
///
/// [Key("id")] pins that name to the one the document publishes. Without it MessagePack writes the
/// C# member name - Id, Title, Done - and a client generated from the document, whose property
/// names are id, title and done, reads none of them.
/// </remarks>
[MessagePackObject(true)]
public partial record Todo(
    [property: Key("id")] int Id,
    [property: Key("title")] string Title,
    [property: Key("done")] bool Done);
#endif
#if (!messagePack)
/// <summary>A todo, as it goes over the wire.</summary>
public record Todo(int Id, string Title, bool Done);
#endif

/// <summary>
/// What a client sends to create one.
/// </summary>
/// <remarks>
/// Separate from <see cref="Todo"/> because the server assigns the id, and a request model carrying
/// one invites a client to choose it. The length constraint is enforced in front of the handler
/// and published as the schema's minLength and maxLength, so the document and the code agree.
#if (messagePack)
///
/// Annotated for MessagePack even though no operation declares it as a response. A request reader
/// is chosen by the inbound Content-Type rather than declared, so a client that sends MessagePack
/// is read as MessagePack on any route with a body.
#endif
/// </remarks>
#if (messagePackKeyed)
[MessagePackObject]
public partial record NewTodo([property: StringLength(1, 64), Key(0)] string Title);
#endif
#if (messagePackNamed)
[MessagePackObject(true)]
public partial record NewTodo([property: StringLength(1, 64), Key("title")] string Title);
#endif
#if (!messagePack)
public record NewTodo([property: StringLength(1, 64)] string Title);
#endif

#endif
/// <summary>
/// Where the todos live.
/// </summary>
/// <remarks>
/// Task-based, because a store is where a real implementation waits on I/O. The handlers await it
/// and return their answers directly; the one in-memory implementation below wraps its answers in
/// Task.FromResult, so that wrapping is written here once rather than around every return.
///
/// An interface, and not only for testability. A handler parameter is bound from the request unless
/// the generator can tell it is a service, and an unattributed concrete class is taken as the body -
/// so injecting TodoStore directly generates DeserializeRequestBody&lt;TodoStore&gt;, and on a route
/// that also takes a real body, two of them. [FromServices] says the same thing explicitly.
/// </remarks>
public interface ITodoStore {

    Task<IReadOnlyList<Todo>> All();

    Task<Todo?> Find(int id);

    Task<bool> TitleExists(string title);

    Task<Todo> Add(string title);

    Task<bool> Remove(int id);
}

/// <summary>
/// In memory, because the point of the sample is the request pipeline rather than storage.
/// </summary>
/// <remarks>
/// [SingletonService] registers this against every interface it implements and against the class
/// itself. The module lists nothing, so registration cannot fall out of step with what exists -
/// which is also what lets a test replace it with [Mock] without changing any wiring here.
#if (specFirst)
///
/// Todo and NewTodo are not declared here: the contract declares them, and the build writes them
/// into Hardened1.Models. Declaring a second pair beside the generated ones is CS0101 on both.
#endif
/// </remarks>
[SingletonService]
public class TodoStore : ITodoStore {

    private readonly Dictionary<int, Todo> _todos = new() {
        [1] = new Todo(1, "Read the generated code", true),
        [2] = new Todo(2, "Add an endpoint", false)
    };

    private int _nextId = 3;

    public Task<IReadOnlyList<Todo>> All() =>
        Task.FromResult<IReadOnlyList<Todo>>(_todos.Values.ToList());

    public Task<Todo?> Find(int id) =>
        Task.FromResult(_todos.TryGetValue(id, out var todo) ? todo : null);

    /// <summary>Titles are unique, which is what gives the sample a real 409.</summary>
    public Task<bool> TitleExists(string title) =>
        Task.FromResult(
            _todos.Values.Any(todo => string.Equals(todo.Title, title, StringComparison.OrdinalIgnoreCase)));

    public Task<Todo> Add(string title) {
        var todo = new Todo(_nextId++, title, false);

        _todos[todo.Id] = todo;

        return Task.FromResult(todo);
    }

    public Task<bool> Remove(int id) => Task.FromResult(_todos.Remove(id));
}
