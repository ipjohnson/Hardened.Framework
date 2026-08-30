# Writing a test

A Hardened test boots the real application. The module graph is applied, configuration is resolved,
startup services run, and the test method's parameters are injected from the resulting provider.

Tests are xUnit tests. `[HardenedTest]` derives from `FactAttribute`, so the runner, the IDE
integration and `dotnet test` all work unchanged.

## The two pieces

**An assembly-level entry point** naming the application module:

```csharp
// Bootstrap.cs
using Hardened.Shared.Testing.Attributes;

[assembly: HardenedTestEntryPoint(typeof(Application))]
```

**A test method** marked `[HardenedTest]`, taking what it needs as parameters:

```csharp
public class MathServiceTests {
    [HardenedTest]
    public void AddsValues(IMathService<int> mathService) {
        Assert.Equal(6, mathService.Add(1, 2, 3));
    }
}
```

Every parameter is resolved from the application's provider. A service the application registers is
a parameter the test can ask for.

## Mocking

`[Mock]` on a parameter substitutes that service for an NSubstitute mock — registered last, so it
wins over the application's own registration — and hands the same instance to the test:

```csharp
using NSubstitute;

public class OrderServiceTests {
    [HardenedTest]
    public async Task FallsBackWhenRatesUnavailable(
        IOrderService orders,
        [Mock] IRateTable rates) {

        rates.Lookup(Arg.Any<string>()).Returns((decimal?)null);

        var order = await orders.Price("SKU-1");

        Assert.Equal(0m, order.Total);
    }
}
```

The mock and the real `IOrderService` are the same graph: `orders` was constructed against the mocked
`IRateTable`.

## `ITestContext`

Ask for `ITestContext` and you get named steps, a retry engine and a logger routed to xUnit's output:

```csharp
public interface ITestContext {
    IRetryEngine Retry { get; }
    CancellationToken CancellationRequest { get; }
    ILogger Logger { get; }

    void Step(Action step, string description, params object[] parameters);
    T Step<T>(Func<T> step, string description, params object[] parameters);
    Task Step(Func<Task> step, string description, params object[] parameters);
    Task<T> Step<T>(Func<Task<T>> step, string description, params object[] parameters);
}
```

A step logs pass or fail and its duration:

```csharp
[HardenedTest]
public async Task PlacesAnOrder(ITestContext context, IOrderService orders) {
    var order = await context.Step(
        () => orders.Create("SKU-1"), "Create an order");

    await context.Step(
        () => orders.Confirm(order.Id), "Confirm it");
}
```

```
pass - Create an order - 12ms
pass - Confirm it - 4ms
```

In a test failing on CI and passing locally, the step names say how far it got and the durations say
whether something was slow rather than broken.

### Retrying

`Retry` polls until a condition holds, rather than sleeping and hoping:

```csharp
public interface IRetryEngine {
    int Delay { get; set; }   // milliseconds, default 1000

    Task TillTrue(Func<Task<bool>> testFunc, string description, params object[] parameters);
    Task TillFalse(Func<Task<bool>> testFunc, string description, params object[] parameters);
    Task<T> TillValue<T>(Func<Task<T>> value, string description, params object[] parameters);
}
```

```csharp
[HardenedTest]
public async Task ProjectionCatchesUp(ITestContext context, IProjection projection) {
    var summary = await context.Retry.TillValue(
        () => projection.Find("SKU-1"), "Projection has the order");

    Assert.Equal("SKU-1", summary.Sku);
}
```

`TillValue` returns as soon as the function produces a non-null result, which is what to use against
anything eventually consistent — a DynamoDB stream, an SQS consumer, a read replica.

## Environments in tests

The environment is named `test` unless the test says otherwise:

```csharp
[HardenedTest]
[EnvironmentName("production")]
[EnvironmentValue("FEATURE_X", "on")]
public void UsesTheProductionSender(IEmailSender sender) {
    Assert.IsType<SmtpEmailSender>(sender);
}
```

`[EnvironmentName]` changes the name that `[IfEnvironment]` and environment-scoped configuration
amenders are evaluated against, so this is how you test a registration that only exists in one
environment. `[EnvironmentValue]` sets a variable for the test alone, without touching the process.

Both attributes work at method, class or assembly level. The narrower one wins.

## Attributes that set up a test

The framework's test harnesses are attributes implementing one or both of these:

```csharp
public interface IHardenedTestDependencyRegistrationAttribute {
    void RegisterDependencies(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceCollection services);
}

public interface IHardenedTestStartupAttribute {
    Task Startup(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceProvider provider);
}
```

`[WebTesting]`, `[LambdaFunctionTesting]` and `[LocalDynamoDb]` are all built this way. Registration
attributes run while the collection is being built, after the application's modules, so a
registration here overrides one there. Startup attributes run after the provider exists, in `Order`,
which is where a container is started or a table created.

Write one when several tests need the same setup:

```csharp
public class SeededDatabaseAttribute : Attribute, IHardenedTestStartupAttribute {
    public int Order => 100;

    public async Task Startup(
        AttributeCollection attributes, MethodInfo method,
        IHardenedEnvironment environment, IServiceProvider provider) {
        await provider.GetRequiredService<ISeeder>().Seed();
    }
}
```

## Where to go next

- [Testing web handlers](/guide/testing-web) — driving routes through the real pipeline
- [Testing AWS handlers](/aws/testing) — Lambda functions, SQS batches, stream records, DynamoDB Local
