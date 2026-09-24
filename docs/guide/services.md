# Registering services

A lifetime attribute such as `[SingletonService]` on a class registers the class in the container.
The build writes the registration into the module of the project that declares the class. No module
lists its services.

The examples add files to an application made with `dotnet new hardened-web -n Todos`.
`src/Todos/VisitCounter.cs` declares a singleton:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Todos;

public interface IVisitCounter
{
    int Next();
}

[SingletonService]
public class VisitCounter : IVisitCounter
{
    private int _count;

    public int Next() => Interlocked.Increment(ref _count);
}
```

A handler parameter typed as an interface is resolved from the container.
`src/Todos/VisitController.cs` declares a handler that takes the counter as `IVisitCounter`:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class VisitController
{
    [Get("/visits")]
    public int Visits(IVisitCounter counter) => counter.Next();
}
```

Every request gets the same counter:

```http
GET /todos/visits

HTTP/1.1 200 OK
Content-Type: application/json

1
```

```http
GET /todos/visits

HTTP/1.1 200 OK
Content-Type: application/json

2
```

## Lifetimes

The attribute decides how many instances the container creates:

| Attribute | Instances |
|---|---|
| `[SingletonService]` | One for the application |
| `[ScopedService]` | One per request |
| `[TransientService]` | A new one each time the service is resolved |

A scoped service is one instance for the whole of a request. The handler class's constructor and
the handler's parameters get the same instance. The next request gets a new one. A transient service
is a new instance for each constructor or parameter that takes it.

The lifetime attributes are in the namespace `DependencyModules.Runtime.Attributes`. They come from
the package `DependencyModules.Runtime`. `Hardened.Shared.Runtime` depends on that package, so a
project needs no DependencyModules package reference of its own.

A lifetime attribute registers nothing in a project that has no `[HardenedModule]`.
[Modules](/guide/modules) covers declaring one.

## Service types

A class with a lifetime attribute and no `As` is registered as one service type. It is not
registered as every interface it implements. The build chooses the service type by these rules:

| The class | Registered as |
|---|---|
| Declares an interface that is not in the list below | The first such interface it declares |
| Declares none, and its base class, or a class above that, implements one | The first such interface, looking up from the base class |
| Neither | The class itself |

The build passes over these interfaces: `IDisposable`, `IAsyncDisposable`, `ICloneable`,
`IComparable`, `IEquatable<T>`, `IConvertible`, `IFormattable`, `ISpanFormattable`, `IParsable<T>`,
`ISpanParsable<T>`, `IEnumerable`, `IEnumerable<T>`, `ISerializable`, `INotifyPropertyChanged`,
`INotifyPropertyChanging` and `INotifyCollectionChanged`.

A class that declares any other interface is not registered as itself. The template's `TodoStore`
resolves as `ITodoStore` and not as `TodoStore`.

`As = typeof(T)` registers the class as `T`. A class carries one attribute for each service type it
is registered as. Each registration of a singleton gets its own instance.

`[CrossWireService]` registers the class as itself and as every interface it declares. All of these
registrations resolve to one instance. Its `Lifetime` property sets the lifetime. The default is
singleton.

## How a registration is added

`Using` on a lifetime attribute chooses how the registration is added. It takes a
`RegistrationType`. The default is `RegistrationType.Add`.

| `Using` | Adds the registration |
|---|---|
| `RegistrationType.Add` | Always, beside any registration of the same service type |
| `RegistrationType.Try` | Only when the service type has no registration yet |
| `RegistrationType.TryEnumerable` | Unless the same class is already registered for the service type |
| `RegistrationType.Replace` | After removing the first registration of the service type |

Within one module, `Try` and `Replace` registrations are added after the module's other
registrations, whatever the classes are called. [Replacing a registration](#replacing-a-registration)
shows `Replace`.

## Services in handlers

A handler's interface parameters are resolved from the request's service scope. A parameter typed
as a class binds from the request body when its name matches no route token. `[FromServices]` on
the parameter resolves it from the container instead. The attribute is in the namespace
`Hardened.Requests.Abstract.Attributes`.

In this version of `src/Todos/VisitCounter.cs`, the class declares no interface, so it is registered
as itself:

```csharp
using DependencyModules.Runtime.Attributes;

namespace Todos;

[SingletonService]
public class VisitCounter
{
    private int _count;

    public int Next() => Interlocked.Increment(ref _count);
}
```

The handler marks its parameter `[FromServices]`:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class VisitController
{
    [Get("/visits")]
    public int Visits([FromServices] VisitCounter counter) => counter.Next();
}
```

Without `[FromServices]`, a class parameter whose type carries a lifetime attribute fails the build
with the error `HRDR007`. For a method `Plain(PlainThing thing)` on `PlainController`, where
`PlainThing` carries `[SingletonService]`, the build reports:

```text
error HRDR007: Parameter 'thing' of 'PlainController.Plain' is read from the request body. A parameter that names no route token and is not an interface binds from the body, and 'PlainThing' is registered as a service by its [SingletonService], [ScopedService] or [TransientService] attribute, so it was never a body. Mark 'thing' [FromServices], or type it as the interface it is registered against.
```

The generator registers each handler class as transient, so its constructor can take services too.
They come from the request's scope.

The build does not report a service that nothing registers. A request that needs it answers 500.
The log names the missing type.

[Parameter binding](/guide/parameter-binding) covers the other sources a parameter binds from.

## Registering by environment

`[IfEnvironment]` and `[IfNotEnvironment]` register a class only in, or only outside, the named
environments. With the classes in `src/Todos/EmailSenders.cs`, `IEmailSender` is
`ConsoleEmailSender` when the environment is `development` or `test`. It is `SmtpEmailSender` in any
other environment.

```csharp
using System.Net.Mail;
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Todos;

public interface IEmailSender
{
    Task Send(string to, string subject);
}

[SingletonService]
[IfEnvironment("development", "test")]
public class ConsoleEmailSender(ILogger<ConsoleEmailSender> logger) : IEmailSender
{
    public Task Send(string to, string subject)
    {
        logger.LogInformation("Email to {To}: {Subject}", to, subject);

        return Task.CompletedTask;
    }
}

[SingletonService]
[IfNotEnvironment("development", "test")]
public class SmtpEmailSender : IEmailSender
{
    public async Task Send(string to, string subject)
    {
        using var client = new SmtpClient("localhost");

        await client.SendMailAsync("todos@example.com", to, subject, "");
    }
}
```

Environment names are compared ignoring case. `Development` matches `development`.
`[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` test an environment variable instead of the
name. They compare values exactly, case included.

| Attribute | Registers the class when |
|---|---|
| `[IfEnvironment("development", "test")]` | The environment's name is one of the names |
| `[IfNotEnvironment("development", "test")]` | The environment's name is none of the names |
| `[IfEnvironmentValue("TODOS_FLAG")]` | The variable has a value |
| `[IfEnvironmentValue("TODOS_MODE", "on")]` | The variable's value is exactly `on` |
| `[IfNotEnvironmentValue("TODOS_FLAG")]` | The variable has no value |
| `[IfNotEnvironmentValue("TODOS_MODE", "on")]` | The variable's value is not exactly `on` |

With `TODOS_MODE=ON`, `[IfEnvironmentValue("TODOS_MODE", "on")]` does not register the class. A
variable set to an empty string counts as having no value.

`[IfEnvironment]` and `[IfNotEnvironment]` take several names in one attribute. A class carries one
of each at most. `[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` can be repeated. Every
condition on a class has to hold.

Within one module, a registration with a condition is added after the registrations without one. A
class with `[IfEnvironment("development")]` therefore wins a single resolve in `development` over an
unconditional class registered as the same service type. The unconditional class serves in every
other environment.

The conditions become an `if` around the registration in the generated code. The `if` runs when
`PopulateServiceCollection` runs at startup, not during the build. It reads the
`IModuleEnvironment` that is registered in the service collection at that time.
`AddHardenedEnvironment` registers the application's environment as one.

[Environments](/guide/environments) covers the environment's name, `AddHardenedEnvironment` and the
name a test runs under. That page also covers calling `AddHardenedEnvironment` before
`PopulateServiceCollection`, and what the conditions read when the call comes after.

## Decorators

`[Decorator]` on a class makes it wrap a registered service. The container returns the decorator
when the service is resolved. The decorator's constructor receives the registered implementation.
`src/Todos/LoggingVisitCounter.cs` declares a decorator for the `IVisitCounter` of the first
example:

```csharp
using DependencyModules.Runtime.Attributes;
using Microsoft.Extensions.Logging;

namespace Todos;

[Decorator]
public class LoggingVisitCounter(IVisitCounter inner, ILogger<LoggingVisitCounter> logger) : IVisitCounter
{
    public int Next()
    {
        var count = inner.Next();

        logger.LogInformation("Visit {Count}", count);

        return count;
    }
}
```

The decorated service is passed to the constructor parameter whose type the decorator also
implements. `Service = typeof(T)` names the decorated service when more than one parameter fits.

A decorator carries no lifetime attribute. It takes the lifetime of the registration it wraps.

A decorator wraps every registration of the service type, including registrations from other
modules. `Order` decides how decorators nest. The lower value sits closer to the implementation.
Decorators from every module are sorted together by `Order`.

The [DependencyModules documentation](https://ipjohnson.github.io/DependencyModules/guide/decorators)
covers generic decorators and `[Decorate]`, which decorates a service from an assembly you do not
control.

## Replacing a registration

When a service type has several registrations, a single resolve returns the last one.
`IEnumerable<T>` returns all of them. The application module is applied after the modules it
imports, so a plain registration in the host project `src/Todos.Host` already wins a single
resolve. [Modules](/guide/modules) covers the order.

`Using = RegistrationType.Replace` removes the earlier registration first, so the service type is
left with one. `src/Todos.Host/FixedVisitCounter.cs` replaces the library's registration of
`IVisitCounter`:

```csharp
using DependencyModules.Runtime.Attributes;
using Todos;

namespace Todos.Host;

[SingletonService(Using = RegistrationType.Replace)]
public class FixedVisitCounter : IVisitCounter
{
    public int Next() => 42;
}
```

```http
GET /todos/visits

HTTP/1.1 200 OK
Content-Type: application/json

42
```

A test's `[Mock]` registers its test double after the application's registrations, so the
application's code receives the double in that test. [Substituting services](/guide/testing-mocks)
covers `[Mock]`.

## Limits

`[Intercept]` does nothing in a Hardened project. The service resolves as the class itself. The
interceptor never runs. The build reports nothing.

## Next

- [Modules](/guide/modules): modules, the order they are applied in, and startup services
- [Parameter binding](/guide/parameter-binding): every source a handler parameter binds from
- [Environments](/guide/environments): the environment `[IfEnvironment]` reads
- [Substituting services](/guide/testing-mocks): replacing a service in a test
