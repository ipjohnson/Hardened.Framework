# Hardened1

An AWS Lambda function on [Hardened](https://github.com/ipjohnson/Hardened.Framework) — a
compile-time, source-generated .NET framework. The entry point AWS calls, the payload
deserialisation and the dependency injection are all written during the build.

## Run it

```bash
dotnet build
dotnet test
```

There is nothing to `dotnet run`. The tests are how this function is exercised locally: they invoke
it through the real pipeline, with no AWS account and nothing to deploy.

## The two projects

| | |
|---|---|
| `src/Hardened1` | The function: its handler, its models and its services. |
| `tests/Hardened1.Tests` | Tests, which invoke the function the way Lambda does. |

There is no separate host project. On Lambda the deployed artifact is this assembly, and the
generator writes the entry point the runtime calls — so the split that a web application makes
between a library and a host has nothing to separate here.

## The handler

`[HardenedFunction]` on a method of a plain class. No base type, no interface, no registration:

```csharp
public class OrderHandler(OrderLog log) {

    [HardenedFunction]
#if (invoke)
    public Task<OrderAccepted> Process(Order order) { ... }
#endif
#if (sqs)
    public Task Process(Order order) { ... }
#endif
}
```

The generator writes an entry point bound to that exact signature. The payload is deserialised into
the parameter, the dependencies come from the container, and a missing registration is a build
error rather than something the first invocation discovers.

#if (invoke)
The return value is serialised back as the invocation's response.
#endif
#if (sqs)
The handler is called once per record in the batch. Returning normally marks that record handled;
throwing reports it as a batch item failure, so only the records that failed are redelivered.
#endif

A service is registered next to the class it belongs to, with `[SingletonService]`,
`[ScopedService]` or `[TransientService]` — the module lists nothing, so it cannot fall out of step.

## Changing the trigger

`src/Hardened1/Application.cs` names the runtime:

```csharp
[HardenedModule]
[LambdaFunctionModule]
#if (sqs)
[SqsLambda]
#endif
public partial class Application;
```

#if (invoke)
`[LambdaFunctionModule]` is the invocation path itself. Adding an event source — `[SqsLambda]` from
`Hardened.Amz.Function.Sqs.Runtime`, for instance — layers batch handling on top of it, and changes
the handler's signature rather than the rest of the application.
#endif
#if (sqs)
`[LambdaFunctionModule]` is the invocation path; `[SqsLambda]` is the event source layered on it.
Swapping the event source changes this file and the handler's signature, and nothing else.
#endif

## Testing

`[HardenedTest]` boots the real application — the module graph, configuration and startup services —
and injects what the test asks for:

```csharp
#if (sqs)
[HardenedTest]
public async Task ARecordReachesTheHandler(TestSqsApp app, OrderLog log) {
    var response = await app.SendMessage(new Order { Id = "A-1", Quantity = 2 });

    Assert.Empty(response.BatchItemFailures);
}
#endif
#if (invoke)
[HardenedTest]
public async Task ThePayloadReachesTheHandler(LambdaTestApp app, OrderLog log) {
    var accepted = await app.Invoke<OrderAccepted>("Process", new Order { Id = "A-1" });

    Assert.Equal("A-1", accepted.Id);
}
#endif
```

That is the real pipeline — deserialisation, the filter chain, the handler — rather than a method
call. Mark a parameter `[Mock]` and that service is substituted for the whole container.

## Reading the generated code

The fastest way to understand any of this is to read what the build wrote. It is ordinary C#, and
`EmitCompilerGeneratedFiles` is already on:

```
src/Hardened1/obj/Debug/net8.0/generated/
```

One directory per generator: the entry point, the handler and the module registration are all
there.

## Where to go next

- [Documentation](https://ipjohnson.github.io/Hardened.Docs)
- `AGENTS.md` in this directory — the invariants and gotchas, for anyone or anything editing the
  code rather than reading it
