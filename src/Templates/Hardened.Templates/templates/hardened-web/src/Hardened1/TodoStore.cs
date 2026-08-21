using DependencyModules.Runtime.Attributes;

namespace Hardened1;

/// <summary>
/// A todo, as it goes over the wire.
/// </summary>
public record Todo(int Id, string Title, bool Done = false);

/// <summary>
/// What a client sends to create one. Separate from <see cref="Todo"/> because the server assigns
/// the id, and a request model that carries one invites a client to choose it.
/// </summary>
public record NewTodo(string Title);

/// <summary>
/// Where the todos live. In memory, because the point of the sample is the request pipeline rather
/// than storage.
/// </summary>
/// <remarks>
/// [SingletonService] registers this against every interface it implements and against the class
/// itself. The module lists nothing, so registration cannot fall out of step with what exists -
/// which is also what lets a test replace it with [Mock] without any wiring here changing.
/// </remarks>
[SingletonService]
public class TodoStore {

    private readonly Dictionary<int, Todo> _todos = new() {
        [1] = new Todo(1, "Read the generated code", true),
        [2] = new Todo(2, "Add an endpoint")
    };

    private int _nextId = 3;

    public Todo? Find(int id) => _todos.TryGetValue(id, out var todo) ? todo : null;

    /// <summary>Titles are unique, which is what gives the sample a real 409.</summary>
    public bool TitleExists(string title) =>
        _todos.Values.Any(todo => string.Equals(todo.Title, title, StringComparison.OrdinalIgnoreCase));

    public Todo Add(string title) {
        var todo = new Todo(_nextId++, title);

        _todos[todo.Id] = todo;

        return todo;
    }

    public bool Remove(int id) => _todos.Remove(id);
}
