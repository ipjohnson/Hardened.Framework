# Substituting services

`[Mock]` on a parameter replaces a service for the whole application and hands the substitute to
the test. The handler, the service that calls it and the test all hold the one instance.

```csharp
using NSubstitute;

public class OrderServiceTests {

    [HardenedTest]
    public async Task FallsBackWhenRatesAreUnavailable(IOrderService orders, [Mock] IRateTable rates) {
        rates.Lookup(Arg.Any<string>()).Returns((decimal?)null);

        var order = await orders.Price("SKU-1");

        Assert.Equal(0m, order.Total);
    }
}
```

`orders` is the application's real `IOrderService`, constructed against the mock. The wiring under
test is the application's own.

## How a mock lands

The substitute is `Substitute.For<T>()` from NSubstitute, registered as a singleton over the
application's registration of `T`. The last registration wins, so the container hands out the
substitute wherever `T` is asked for. The test receives the same instance, so a `Returns` set up
in the test and a `Received()` check afterwards are against what the application used.

NSubstitute arrives with `Hardened.Shared.Testing`. The test project adds `using NSubstitute;`
and nothing else.

## Behind a route

A handler resolves from the same container, so a mock reaches it through `ITestWebApp` and
through a typed client alike:

```csharp
[HardenedTest]
public async Task CreateTodo_StoresTheTodo(TodosClient client, [Mock] ITodoStore store) {
    store.Add("ship it").Returns(new Todo(7, "ship it", false));

    var created = await client.Todos.PostAsync(new ClientModels.NewTodo { Title = "ship it" })
        .Returns<Created<ClientModels.Todo>>();

    Assert.Equal("/todos/7", created.Location);
}
```

The id came from the mock, so the handler used it.

## A fake instead of a mock

A substitute with behaviour of its own is a class. A test attribute registers it once for every
test that carries the attribute:

```csharp
public sealed class FixedClock : TimeProvider {
    private DateTimeOffset _now = new(2026, 9, 5, 9, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

public sealed class FixedClockAttribute : Attribute, IHardenedTestDependencyRegistrationAttribute {

    public int Order => 0;

    public void RegisterDependencies(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceCollection services) {
        services.AddSingleton<FixedClock>();
        services.AddSingleton<TimeProvider>(provider => provider.GetRequiredService<FixedClock>());
    }
}
```

```csharp
[HardenedTest]
[FixedClock]
public async Task ACachedAnswerExpires(ITestWebApp app, FixedClock clock, [Mock] IRateSource rates) {
    rates.Latest("EUR").Returns(1.10m, 1.20m);

    var first = (await app.Get("/rates/EUR")).Deserialize<Rate>();

    clock.Advance(TimeSpan.FromHours(2));

    var second = (await app.Get("/rates/EUR")).Deserialize<Rate>();

    Assert.Equal(1.20m, second.Value);
}
```

The handler behind `/rates/EUR` caches for an hour. Without the `Advance` the second request is a
hit and reads `1.10m` again.

Registration attributes run after the application's modules, so a registration here replaces one
there. They are valid on a method, a class or the assembly.
[Writing a test attribute](/guide/testing-attributes) has the other interfaces, and
[Testing a duration](/guide/response-caching#testing-a-duration) is the response cache's side of
this example.

## Choosing a registration by environment

A registration that exists only in one environment is reached by naming that environment:

```csharp
[SingletonService(As = typeof(IEmailSender))]
[IfNotEnvironment("development", "test")]
public class SmtpEmailSender : IEmailSender { }
```

```csharp
[HardenedTest]
[EnvironmentName("production")]
public void UsesTheProductionSender(IEmailSender sender) {
    Assert.IsType<SmtpEmailSender>(sender);
}
```

The environment is registered before the modules are applied, so `[IfEnvironment]` is decided
against the test's name. [Environments in tests](/guide/testing#environments-in-tests) has the
attributes.

## Next

- [Writing a test attribute](/guide/testing-attributes): the five setup interfaces
- [Sending requests](/guide/testing-web): a mock behind a route
- [Typed clients](/guide/testing-clients): a mock behind a generated client
