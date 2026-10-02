# Data access

Hardened has no data layer of its own. An application registers EF Core, Dapper or any other
library in a module's `ConfigureServices`, and its handlers take the store as a service. This page
covers four things that work differently in a Hardened application: where the connection string
comes from, which lifetime each part gets, how the schema is created in tests, and how
`dotnet ef` finds the context.

The examples replace the in-memory `TodoStore` of `dotnet new hardened-web -n Todos` with SQLite.
They keep `ITodoStore`, `Todo` and `NewTodo` in `src/Todos/TodoStore.cs`, and remove the
`TodoStore` class. The handlers do not change.

## The connection string

A [configuration model](/guide/configuration) reads the connection string from an environment
variable. `src/Todos/TodoDatabaseOptions.cs` declares it:

```csharp
using Hardened.Shared.Runtime.Attributes;

namespace Todos;

[ConfigurationModel]
public partial class TodoDatabaseOptions
{
    [FromEnvironmentVariable("TODOS_DATABASE")]
    private string _connectionString = "Data Source=todos.db";
}
```

The generator registers `IOptions<ITodoDatabaseOptions>`. A deployment sets `TODOS_DATABASE` in
the function's or the service's configuration. A test sets it through its environment, which
[A database for each test](#a-database-for-each-test) covers. In a test, the process's own
`TODOS_DATABASE` is not read.

## EF Core

The library project references the provider. The template manages versions centrally, so each
package also needs a `PackageVersion` line in `Directory.Packages.props`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
```

The template targets `net8.0`. EF Core 8 and 9 run on it. EF Core 10 needs `net10.0`.

`src/Todos/TodoDbContext.cs` declares the context:

```csharp
using Microsoft.EntityFrameworkCore;

namespace Todos;

public class TodoDbContext(DbContextOptions<TodoDbContext> options) : DbContext(options)
{
    public DbSet<Todo> Todos => Set<Todo>();
}
```

The library module registers it. The template's `TodosLibrary` already implements
`IServiceCollectionConfiguration`, so the registration goes beside its JSON resolver:

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);

    services.AddDbContext<TodoDbContext>(
        (provider, options) =>
            options.UseSqlite(
                provider
                    .GetRequiredService<IOptions<ITodoDatabaseOptions>>()
                    .Value.ConnectionString
            )
    );
}
```

The lambda runs each time a context is created, so it reads the model from the container the
request runs in. The file also needs `using Microsoft.EntityFrameworkCore;` and
`using Microsoft.Extensions.Options;`.

`src/Todos/EfTodoStore.cs` implements `ITodoStore` over the context:

```csharp
using DependencyModules.Runtime.Attributes;
using Microsoft.EntityFrameworkCore;

namespace Todos;

[ScopedService]
public class EfTodoStore(TodoDbContext db) : ITodoStore
{
    public async Task<IReadOnlyList<Todo>> All() =>
        await db.Todos.AsNoTracking().OrderBy(todo => todo.Id).ToListAsync();

    public Task<Todo?> Find(int id) =>
        db.Todos.AsNoTracking().FirstOrDefaultAsync(todo => todo.Id == id);

    public Task<bool> TitleExists(string title) =>
        db.Todos.AnyAsync(todo => todo.Title.ToLower() == title.ToLower());

    public async Task<Todo> Add(string title)
    {
        var todo = new Todo(0, title, false);

        db.Todos.Add(todo);
        await db.SaveChangesAsync();

        return todo;
    }

    public async Task<bool> Remove(int id) =>
        await db.Todos.Where(todo => todo.Id == id).ExecuteDeleteAsync() > 0;
}
```

### Lifetimes

`AddDbContext` registers the context as scoped. Each request gets its own context, and a context
is not safe to use from two threads at once. A service that takes the context must be scoped or
transient too. `EfTodoStore` is `[ScopedService]` for that reason.

A `[SingletonService]` must not take the context, or a scoped service that holds one. The container
is built without scope validation ([#513](https://github.com/ipjohnson/Hardened.Framework/issues/513)),
so nothing refuses the registration. The application builds, starts and passes its tests. Under
concurrent requests, the one context is shared, and EF Core throws "A second operation was started
on this context instance".

A singleton that needs the database creates a scope for each piece of work. A health check is a
singleton, so it takes `IServiceScopeFactory`:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Web.Runtime.Health;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Todos;

[SingletonService]
public class DatabaseHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public string Name => "database";

    public async Task<HealthCheckResult> Check(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy("the database cannot be reached");
    }
}
```

[Hosts](/guide/hosts#health-endpoints) covers health checks.

### The schema at startup

A [startup service](/guide/modules#run-code-at-startup) creates the schema and seeds the two todos
the template starts with. A startup service is a singleton too, so it creates a scope from the
provider it receives:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Todos;

[SingletonService]
public class TodoDatabaseStartup : IStartupService
{
    public async Task<bool> Startup(IServiceProvider rootProvider)
    {
        await using var scope = rootProvider.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        await db.Database.MigrateAsync();

        if (!await db.Todos.AnyAsync())
        {
            db.Todos.AddRange(
                new Todo(1, "Read the generated code", true),
                new Todo(2, "Add an endpoint", false)
            );

            await db.SaveChangesAsync();
        }

        return true;
    }
}
```

`MigrateAsync` applies the migrations the database does not have yet. It needs at least one
migration, which [Design-time tools](#design-time-tools) creates. `EnsureCreatedAsync` creates the
schema from the model with no migrations. Use one or the other. A database that
`EnsureCreatedAsync` created has no migrations history, and `MigrateAsync` then fails on it.

Every startup service has to be safe to run again against a database that already has its schema
and its seed data. The pipeline test host runs the startup services in every container it builds,
which is once for each request. [Writing a test](/guide/testing#a-container-for-each-request)
covers the container per request. `MigrateAsync` and `EnsureCreatedAsync` are both safe to repeat.
The seed checks before it inserts.

Migrating at startup suits one instance. With several instances starting at once, apply the
migrations as a deployment step, with `dotnet ef database update` or a migration bundle, and leave
`MigrateAsync` out of the startup service.

### Design-time tools

`dotnet ef migrations add` with `--startup-project src/Todos.Host` runs the host's `Program.Main`.
The tool loads the startup project and looks for an `IHost` it can take the services from. When the
startup project can load `Microsoft.Extensions.Hosting` 6.0 or later, the tool runs `Main` on a
background thread and waits for it to build one. A Hardened `Program.cs` builds no `IHost`. `Main`
starts the server and runs the startup services, which can apply migrations to a database in the
host folder. After five minutes the tool prints this and carries on without the application's
services:

```text
An error occurred while accessing the Microsoft.Extensions.Hosting services. Continuing without the application service provider. Error: Timed out waiting for the entry point to build the IHost after 00:05:00. This timeout can be modified using the 'DOTNET_HOST_FACTORY_RESOLVER_DEFAULT_TIMEOUT_IN_SECONDS' environment variable.
```

The `AddDbContext` call in the module never reaches the tool. A command that needs a context then
fails:

```text
Unable to create a 'DbContext' of type 'TodoDbContext'. The exception 'Unable to resolve service for type 'Microsoft.EntityFrameworkCore.DbContextOptions`1[Todos.TodoDbContext]' while attempting to activate 'Todos.TodoDbContext'.' was thrown while attempting to create an instance.
```

An `IDesignTimeDbContextFactory<TodoDbContext>` tells the tool how to create the context. It goes
in the library, in `src/Todos/TodoDbContextFactory.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Todos;

public class TodoDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args) =>
        new(
            new DbContextOptionsBuilder<TodoDbContext>()
                .UseSqlite(
                    Environment.GetEnvironmentVariable("TODOS_DATABASE") ?? "Data Source=todos.db"
                )
                .Options
        );
}
```

The interface is in the provider's dependencies, so the library needs no other package to compile
it. Point `--startup-project` at the library as well, so that no Hardened entry point is loaded at
all. The startup project needs `Microsoft.EntityFrameworkCore.Design`:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" PrivateAssets="all" />
```

```bash
dotnet ef migrations add Initial --project src/Todos --startup-project src/Todos
```

The command finishes in seconds and writes the migration to `src/Todos/Migrations`. A separate
console project that references the library and `Microsoft.EntityFrameworkCore.Design`, with an
empty `Main`, works as the startup project too.

With `--startup-project src/Todos.Host`, the factory's effect depends on the EF Core version:

| EF Core | With the factory |
|---|---|
| 9 and later | The tool uses the factory and does not run `Main`, when the factory covers every context it finds. With two contexts, name one with `--context`, or the tool runs `Main` again |
| 8 | The tool still runs `Main` and waits for the timeout, then uses the factory |

`DOTNET_HOST_FACTORY_RESOLVER_DEFAULT_TIMEOUT_IN_SECONDS` sets the wait in seconds. Set to `1`, it
shortens the five minutes to one second. `Main` still runs, and the server it starts stays up until
the command exits. The variable does not remove the need for the factory.

These results are from `dotnet-ef` 10.0.12. The EF Core 8 and 9 rows are from the tool's source on
those release branches.

## Dapper

The library project references Dapper and the SQLite driver:

```xml
<PackageReference Include="Dapper" />
<PackageReference Include="Microsoft.Data.Sqlite" />
```

A singleton hands out connections. It holds the connection string and no connection, so it is safe
to share:

```csharp
using DependencyModules.Runtime.Attributes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace Todos;

public interface ITodoDatabase
{
    SqliteConnection Open();
}

[SingletonService]
public class TodoDatabase(IOptions<ITodoDatabaseOptions> options) : ITodoDatabase
{
    public SqliteConnection Open() => new(options.Value.ConnectionString);
}
```

The store opens a connection for each call. Dapper opens a closed connection and closes it again:

```csharp
using Dapper;
using DependencyModules.Runtime.Attributes;

namespace Todos;

[SingletonService]
public class DapperTodoStore(ITodoDatabase database) : ITodoStore
{
    public async Task<IReadOnlyList<Todo>> All()
    {
        await using var connection = database.Open();

        var rows = await connection.QueryAsync<TodoRow>(
            "SELECT Id, Title, Done FROM Todos ORDER BY Id"
        );

        return rows.Select(row => row.ToTodo()).ToList();
    }

    public async Task<Todo?> Find(int id)
    {
        await using var connection = database.Open();

        var row = await connection.QuerySingleOrDefaultAsync<TodoRow>(
            "SELECT Id, Title, Done FROM Todos WHERE Id = @id",
            new { id }
        );

        return row?.ToTodo();
    }

    public async Task<bool> TitleExists(string title)
    {
        await using var connection = database.Open();

        return await connection.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM Todos WHERE Title = @title COLLATE NOCASE)",
            new { title }
        );
    }

    public async Task<Todo> Add(string title)
    {
        await using var connection = database.Open();

        var id = await connection.ExecuteScalarAsync<int>(
            "INSERT INTO Todos (Title, Done) VALUES (@title, 0); SELECT last_insert_rowid();",
            new { title }
        );

        return new Todo(id, title, false);
    }

    public async Task<bool> Remove(int id)
    {
        await using var connection = database.Open();

        return await connection.ExecuteAsync("DELETE FROM Todos WHERE Id = @id", new { id }) > 0;
    }

    private sealed class TodoRow
    {
        public int Id { get; set; }

        public string Title { get; set; } = "";

        public bool Done { get; set; }

        public Todo ToTodo() => new(Id, Title, Done);
    }
}
```

`TodoRow` is there because SQLite returns every integer as `Int64`. Dapper converts a column to a
property's type, but it maps to a record's constructor only when the column types match the
parameters exactly.

The startup service writes its DDL so that a second run changes nothing:

```csharp
using Dapper;
using DependencyModules.Runtime.Attributes;
using Hardened.Shared.Runtime.Application;

namespace Todos;

[SingletonService]
public class TodoSchemaStartup(ITodoDatabase database) : IStartupService
{
    public async Task<bool> Startup(IServiceProvider rootProvider)
    {
        await using var connection = database.Open();

        await connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS Todos (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Done INTEGER NOT NULL
            );

            INSERT OR IGNORE INTO Todos (Id, Title, Done) VALUES
                (1, 'Read the generated code', 1),
                (2, 'Add an endpoint', 0);
            """
        );

        return true;
    }
}
```

A plain `CREATE TABLE` fails the second time it runs. In a test on the pipeline host, that is the
second request.

## A database for each test

The template's tests rely on the container per request: what one request writes to the in-memory
store is gone by the next. A database outlives the container. Every request in a test then sees
what the earlier ones wrote, and two tests that share a database file see each other's writes.

An attribute that implements `IHardenedTestEnvironmentAttribute` gives each test its own file.
`tests/Todos.Tests/TestDatabaseAttribute.cs` names the file after the test method:

```csharp
using System.Reflection;
using Hardened.Shared.Testing.Attributes;

namespace Todos.Tests;

public sealed class TestDatabaseAttribute : Attribute, IHardenedTestEnvironmentAttribute
{
    public void ConfigureEnvironment(
        AttributeCollection attributeCollection,
        MethodInfo methodInfo,
        string environmentName,
        IDictionary<string, object> environment
    )
    {
        var directory = Path.Combine(Path.GetTempPath(), "todos-tests");

        Directory.CreateDirectory(directory);

        var path = Path.Combine(
            directory,
            $"{methodInfo.DeclaringType!.FullName}.{methodInfo.Name}.db"
        );

        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            File.Delete(file);
        }

        environment["TODOS_DATABASE"] = $"Data Source={path}";
    }
}
```

The runner calls the attribute once for each test, when it builds the test's environment. Every
container the test builds shares that environment. Each request therefore reads the same
`TODOS_DATABASE` and opens the same file. The attribute deletes the file left by the test's last
run, so each run starts from the schema and the seed.

The path has to be fixed for the test, not chosen at random. A random path chosen inside the
`AddDbContext` lambda is chosen again for every scope. The startup service then migrates one file,
and the request's context opens another with no tables. The first request fails with
`SQLite Error 1: 'no such table: Todos'`.

The rows of a theory share one file, because they share one method. A test class runs its tests one
after another, and the class name in the path keeps two classes apart when they run in parallel.

`[assembly: TestDatabase]` in `tests/Todos.Tests/Bootstrap.cs` covers every test in the project.
That file declares no namespace, so it also needs `using Todos.Tests;`.
[Substituting services](/guide/testing-mocks#writing-a-test-attribute) covers the attribute
interfaces.

With the attribute in place, what one request writes is there for the next:

```csharp
using DependencyModules.xUnit.Attributes;
using Hardened.Web.Testing;
using Xunit;

namespace Todos.Tests;

public class TodoDatabaseTests
{
    [ModuleTest]
    public async Task ATodoOneRequestCreatesIsThereForTheNext(ITestWebApp app)
    {
        await app.Post(new NewTodo("Write a test"), "/todos");

        var todos = (await app.Get("/todos")).Deserialize<List<Todo>>();

        Assert.Equal([1, 2, 3], todos.Select(todo => todo.Id));
    }
}
```

The template's `ATodoOneRequestCreatesIsGoneByTheNext`, in `ContainerIsolationTests`, asserts the
opposite and fails once the store is a database. Delete it, because `TodoDatabaseTests` replaces
it.

`Data Source=:memory:` does not work across requests. Each SQLite connection to `:memory:` opens a
database of its own, and each request opens its own connection.

## Next

| Page | Covers |
|---|---|
| [Configuration](/guide/configuration) | Configuration models and `[FromEnvironmentVariable]` |
| [Registering services](/guide/services) | The lifetime attributes |
| [Modules](/guide/modules) | `ConfigureServices` and startup services |
| [Writing a test](/guide/testing) | The container per request, and what every request shares |
| [Substituting services](/guide/testing-mocks) | Test attributes, and replacing the store with a mock |
| [DynamoDB client](/aws/dynamodb) | DynamoDB, and DynamoDB Local in a test |
