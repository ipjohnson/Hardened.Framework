# Registering services

A lifetime attribute on the class registers it. The generator emits the `IServiceCollection`
calls during the build, and the module lists nothing.

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

```csharp
public class IntMathController {
    [Post("/int/add")]
    public int Add(IMathService<int> mathService, MathAddModel model) =>
        mathService.Add(model.Values?.ToArray() ?? []);
}
```

`mathService` comes from the request's service scope and `model` from the body. The generator
decides which is which during the build; see [Parameter binding](/guide/parameter-binding).

## Lifetimes

| Attribute | Lifetime |
|---|---|
| `[SingletonService]` | One instance for the application |
| `[ScopedService]` | One instance per request |
| `[TransientService]` | A new instance each time it is resolved |

With no arguments, the class registers as every interface it implements. `As` narrows it to one
service type:

```csharp
[SingletonService(As = typeof(IDynamoDbClientProvider))]
public sealed class DynamoDbClientProvider : IDynamoDbClientProvider, IDisposable { }
```

A service is registered by the module in whose assembly it is compiled. Which assemblies come
along is what [importing a module](/guide/modules#composing-modules) decides.

## Injecting into handlers

A handler's constructor is resolved from the container like any other class. A handler method can
also take services as parameters, so a controller does not accumulate constructor arguments that
one method uses. Mark a parameter `[FromServices]` to state the choice when the generator cannot
infer it.

## Conditional registration

A registration can depend on the environment without moving the decision to run time:

```csharp
[SingletonService(As = typeof(IEmailSender))]
[IfEnvironment("development", "test")]
public class ConsoleEmailSender : IEmailSender { }

[SingletonService(As = typeof(IEmailSender))]
[IfNotEnvironment("development", "test")]
public class SmtpEmailSender : IEmailSender { }
```

`[IfEnvironmentValue]` and `[IfNotEnvironmentValue]` test a named variable rather than the
environment name. All four read the same `IHardenedEnvironment` the rest of the application sees.
See [Environments](/guide/environments).

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
covers ordering, generic decorators and generated interceptors.

## Overriding a registration

The last registration wins. That is how a test's [`[Mock]`](/guide/testing-mocks) replaces a
service, and how [`[LocalDynamoDb]`](/aws/testing#dynamodb-local) puts a container-backed
`IDynamoDbClientProvider` over the application's own.

A self-hosting entry point takes the same override in its constructor:

```csharp
var application = new Application(
    new EnvironmentImpl("test"),
    (environment, services) => services.AddSingleton<IClock>(new FixedClock(...)));
```

## Next

- [Modules](/guide/modules): which assemblies a module brings along
- [Parameter binding](/guide/parameter-binding): how a handler parameter is told apart from the body
- [Substituting services](/guide/testing-mocks): replacing a registration in a test
