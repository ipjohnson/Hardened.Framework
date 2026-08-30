# Registering services

Service registration is [DependencyModules](https://ipjohnson.github.io/DependencyModules/), which
Hardened builds on. Registration is declared next to the class it belongs to, and the generator
emits the `IServiceCollection` calls during the build.

## Attributing a service

```csharp
using DependencyModules.Runtime.Attributes;

public interface IMathService<T> {
    T Add(params T[] values);
}

[TransientService]
public class IntMathService : IMathService<int> {
    public int Add(params int[] values) => values.Sum();
}
```

Three lifetimes, matching the container's own:

| Attribute | Lifetime |
|---|---|
| `[SingletonService]` | One instance for the application |
| `[ScopedService]` | One instance per request |
| `[TransientService]` | A new instance each time it is resolved |

With no arguments, the class registers as every interface it implements. `As` narrows that to one
service type:

```csharp
[SingletonService(As = typeof(IDynamoDbClientProvider))]
public sealed class DynamoDbClientProvider : IDynamoDbClientProvider, IDisposable { }
```

## Injecting into handlers

A handler's constructor is resolved from the container like any other class. A request handler can
also take services as *method* parameters, which keeps a controller from accumulating constructor
arguments only one method uses:

```csharp
public class IntMathController {
    [Post("/int/add")]
    public int Add(IMathService<int> mathService, MathAddModel model) {
        return mathService.Add(model.Values?.ToArray() ?? Array.Empty<int>());
    }
}
```

`mathService` comes from the request's service scope; `model` is deserialised from the body. The
generator decides which is which during the build — see
[Parameter binding](/guide/parameter-binding). Mark a parameter `[FromServices]` to state the
choice.

## What the module has to say about it

Nothing. A service marked with a lifetime attribute is registered by the module in whose assembly it
is compiled. What the module *does* control is which assemblies come along, which is what
[importing another module](/guide/modules#composing-modules) does.

## Conditional registration

Registration can depend on the environment without moving the decision to run time:

```csharp
[SingletonService(As = typeof(IEmailSender))]
[IfEnvironment("development", "test")]
public class ConsoleEmailSender : IEmailSender { }

[SingletonService(As = typeof(IEmailSender))]
[IfNotEnvironment("development", "test")]
public class SmtpEmailSender : IEmailSender { }
```

`[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` test a named variable rather than the
environment name. All four are evaluated against the same `IHardenedEnvironment` the rest of the
application sees.

## Decorators and interception

Both come from DependencyModules and both work here:

```csharp
[SingletonService(As = typeof(IOrderRepository))]
[Decorator(Order = 10)]
public class CachingOrderRepository : IOrderRepository {
    public CachingOrderRepository(IOrderRepository inner) { /* … */ }
}
```

The [DependencyModules documentation](https://ipjohnson.github.io/DependencyModules/guide/decorators)
covers ordering, generic decorators and generated interceptors in full.

## Overriding a registration

The last registration wins, which is what makes test substitution work. `[LocalDynamoDb]` registers
its container-backed `IDynamoDbClientProvider` after the application's modules have run, and the
container hands out the container-backed one.

The same is available to any test through the `overrideDependencies` parameter on a self-hosting
entry point's constructor:

```csharp
var application = new Application(
    new EnvironmentImpl("test"),
    (environment, services) => services.AddSingleton<IClock>(new FixedClock(...)));
```
