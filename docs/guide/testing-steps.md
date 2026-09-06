# Steps and retries

`ITestContext` names the steps of a test and polls for a condition instead of sleeping.

```csharp
[HardenedTest]
public async Task PlacesAnOrder(ITestContext context, IOrderService orders, IProjection projection) {
    var order = await context.Step(() => orders.Create("SKU-1"), "Create an order");

    await context.Step(() => orders.Confirm(order.Id), "Confirm it");

    var summary = await context.Retry.TillValue(
        () => projection.Find(order.Id), "Projection has the order");

    Assert.Equal("SKU-1", summary.Sku);
}
```

```
pass - Create an order - 12ms
pass - Confirm it - 4ms
```

In a test that fails on CI and passes locally, the step names say how far it got and the durations
say whether something was slow rather than broken.

## Steps

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

A step logs pass or fail and its duration, and returns what the delegate returned. The
`parameters` are logged with the description.

## Retries

`Retry` polls until a condition holds:

```csharp
public interface IRetryEngine {
    int Delay { get; set; }   // milliseconds between attempts, default 1000

    Task TillTrue(Func<Task<bool>> testFunc, string description, params object[] parameters);
    Task TillFalse(Func<Task<bool>> testFunc, string description, params object[] parameters);
    Task<T> TillValue<T>(Func<Task<T>> value, string description, params object[] parameters);
}
```

`TillValue` returns as soon as the function produces a non-null result. It is the one to use
against anything eventually consistent: a DynamoDB stream, an SQS consumer, a read replica.

## The logger and the token

`Logger` is routed to the runner's output, so a line logged from a test appears with that test.
`CancellationRequest` is the test's own token, for passing to whatever the test awaits.

## ITestWebApp is an ITestContext

`ITestWebApp` extends `ITestContext`, so a web test has steps and retries on the parameter it
already holds:

```csharp
[HardenedTest]
public async Task PublishesARateSet(ITestWebApp app) {
    await app.Step(() => app.Post(new RateSet("EUR", 1.10m), "/rates"), "Publish the rates");

    await app.Retry.TillTrue(
        async () => (await app.Get("/rates/EUR")).StatusCode == 200, "The rate is readable");
}
```

## Next

- [Writing a test](/guide/testing): the parameters a test can take
- [Testing AWS handlers](/aws/testing): where retries are most often needed
