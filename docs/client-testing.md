# Client testing

How a test asserts through a generated client, and what the two packages that make it possible
do. `docs/testing-conventions.md` says what to assert; this says how a Kiota client or a Refit
interface becomes a test parameter and how a call through one is asserted in the contract's own
vocabulary.

Written 2026-09-05, with `Hardened.Kiota.Testing` and `Hardened.Refit.Testing`.

---

## The shape

```csharp
[assembly: KiotaTesting]          // or [assembly: RefitTesting], or both

public class TodoTests {

    [HardenedTest]
    public async Task CreateTodo_AnswersCreated(TodosClient client) {
        var created = await client.Todos.PostAsync(new NewTodo { Title = "ship it" })
            .Returns<Created<Todo>>();

        Assert.Equal($"/todos/{created.Value.Id}", created.Location);
    }

    [HardenedTest]
    public async Task UnknownTodo_IsNotFound(TodosClient client) {
        var missing = await client.Todos[9999].GetAsync().Returns<NotFound<Problem>>();

        Assert.Contains("9999", missing.Body.Detail);
    }
}
```

Two things happened there. The assembly attribute made every client of that generator's shape a
test parameter, built over the pipeline with the test's credential on it and nothing written per
client. And `Returns<T>()` named the response type the contract declares - the status, the body
type and the headers that status carries, in one word - and handed back an instance of it built
from what the client actually received. `Returns<T>()` itself is `Hardened.Web.Testing`'s: it awaits
the call and hands what came back to the routes the assembly named, and the route that recognises
the answer reads it. That is what makes the call site one expression for both generators, and what
lets a solution with a Kiota client and a Refit interface declare both attributes.

The vocabulary is `Hardened.Requests.Abstract.Responses`: `Created<T>`, `Ok<T>`, `NoContent`,
`NotFound<T>`, `Conflict<T>`, `RateLimited<T>` and the rest, the same types a handler returns. A
test and the handler it exercises read the same word for the same answer.

## Why a package per generator

The two generators produce different things, and the difference is structural rather than
cosmetic.

A **Kiota** method returns the body for a success and throws a generated model for a declared
refusal. The thrown model carries the status and the response headers, so a refusal is read off
it directly - which is what makes a client that stopped surfacing the refusal fail here. A success
carries nothing but the body: the 201 and its `Location`, the 204, the `ETag` on a 304 are all
gone by the time the generated method returns. The route this package registers therefore builds
the client over an `HttpClient` whose chain has one handler of the package's own in it, recording
the response as the client's HTTP stack saw it, one hop before the generated code read it. That
is the only way to see the headers Kiota discards: Kiota's own `HeadersInspectionHandlerOption`
accumulates across calls when shared, so a per-call reading is the only correct lifetime, and a
`Task` extension cannot own the call.

A **Refit** method declared `Task<IApiResponse<T>>` - Refitter's `--use-api-response` - returns
an envelope for every status and throws for none. Status, headers, body and error all arrive on
one object, so nothing needs recording and the helper is a plain read. A method declared
`Task<T>` throws an `ApiException` for a refusal, which carries the same three and is read the
same way, and returns the body alone for a success; that shape has discarded the status, so
`Returns` refuses it by name rather than guessing a 200. Refit has no error mapping, so an error
body arrives as text and is read as the expectation's type argument - the `Problem` in
`NotFound<Problem>` - through the client's own `RefitSettings`, so the assertion sees what a
consumer of the client would.

What the two share is everything on either side of those three values. `Returns` in
`Hardened.Web.Testing` awaits the call, hands the result or the exception to each route the
assembly named until one answers with a `ClientAnswer`, and gives that to
`ResponseExpectation.Match<TExpected>` in `Hardened.Requests.Abstract`, which compares the status
to `TExpected.StatusCode` and calls `TExpected.FromResponse(body, headers)`. Every failure message
is written once, so a wrong status reads the same whichever client reported it:

```
Expected 404 (NotFound<Problem>), the call was answered 409 carrying a Problem.
```

## The route seam

`Hardened.Web.Testing` builds a typed test parameter by three routes, tried in order, and reads a
call's answer through the same routes:

1. A public `ITestClientFactory<TClient>` in the test assembly, for that one client.
2. An `ITestClientRoute` the assembly named in `[assembly: TestClientRoute(typeof(...))]`, which
   answers for a whole shape of client. `[assembly: KiotaTesting]` and `[assembly: RefitTesting]`
   are that attribute with the route filled in.
3. A single public constructor taking exactly one `HttpClient`.

The factory wins over the route, so a test project that wants one client built its own way - a
real authentication provider under test, a middleware handler, its own `RefitSettings` - declares
a factory for that client and keeps the route for the rest. A client none of the three can build
fails naming all three.

A route builds over a `TestClientContext`: the harness's own `HttpClient`, already carrying the
credential, or a fresh one over handlers of the route's choosing through `CreateHttpClient`. A
route that also implements `ITestClientReader` reads answers: `Returns` asks each reader in the
order the assembly named them, a reader answers with a `ClientAnswer` - status, body, headers, and
a caveat for a failure to carry - for the shapes it recognises and null for anything else, and a
failure no reader recognises reaches the test as it was thrown. The harness names no generator and
references none; the generator-specific knowledge lives in the generator's package, which is what
lets a solution with both a Kiota client and a Refit interface declare both attributes and have
each route answer for the clients it recognises.

## What `Returns` reads

| | Kiota | Refit |
|---|---|---|
| Refusal | the thrown model: status and headers off it, the model as the body | the envelope's `Error`, or the thrown `ApiException`: status and headers off it, `Content` read as the expectation's type argument |
| Success | the returned value as the body; status and headers from the recorded response | the envelope: status, headers and `Content` |
| Success returned alone | the recording covers it | refused by name; declare the method `Task<IApiResponse<T>>` |
| Undeclared refusal | a bare `ApiException`: status only, and the failure says why there is no body | as any refusal |

The recording is per running xUnit test, keyed the way `LastResponse` is, so parallel tests never
read each other's. Within one test it is the most recent call, which is what
`await client.X.PostAsync(...).Returns<...>()` asks about; awaiting several calls at once and then
asserting on one is not a shape it can answer. A client built by a factory of the test project's
own, or by hand inside the test, has no recorder in its chain, and a success through it cannot be
read - the failure says so and names the attribute.

`ReturnsStatus<T>()` asserts the status alone, for the response types that are not expectations
because they state something the wire does not carry back - `NotFound` naming the resource,
`Conflict` its detail line - and for a refusal the document declares no body for.

## Custom response types

An application's own response type is usable with `Returns` once it implements
`IResponseExpectation<TSelf>`: `StatusCode`, and `FromResponse(body, headers)` reading the headers
that are its own. For the Refit package one convention applies on top: a type that carries a body
declares one type argument for it, because that argument is what the error text is read as.
`Status<TCode, TBody>` follows it; a marker implementing `IStatusCode` is never taken for the body.

## Over a socket

A client is the same parameter on a socket host. `[KestrelHost]` on a test, a class or the
assembly runs the application on Kestrel over the test's own container, on a loopback port the
kernel picks, and `[AspNetCoreHost]` runs it inside the real ASP.NET Core pipeline the same way;
every `HttpClient` the harness hands out sends there: a route builds its client
over the host's handler and reads `TestClientContext.BaseAddress`, which is the bound address
rather than `http://harness/`, so a Kiota client's `BaseUrl` and a Refit interface's relative paths
resolve against the socket without a change in either package. `Returns<T>()` reads the same
answer, because Kiota's recorder sits in the client's own chain and Refit's envelope carries what
it always carried, and `LastResponse` is recorded from what came back over the wire. What changes
is what the wire changes: `TestWebResponse.Failure` is null, and the headers are the ones Kestrel
wrote. `KestrelHostTests` in the web SUT's test project runs the shapes above through it, and the
NUnit project beside it runs them again on the other runner.

## Where the code is

| | |
|---|---|
| `src/Web/Hardened.Web.Testing/ClientAssertions.cs` | `Returns<T>()` and `ReturnsStatus<T>()`, over `ITestClientReader` and `ClientAnswer` |
| `src/Web/Hardened.Web.Testing/ITestClientRoute.cs` | the seam, `TestClientContext`, `TestClientRouteAttribute` |
| `src/Clients/Hardened.Kiota.Testing` | `KiotaTestingAttribute`, `KiotaClientRoute` as route and reader, the recording handler |
| `src/Clients/Hardened.Refit.Testing` | `RefitTestingAttribute`, `RefitClientRoute` as route and reader, `RefitAnswers` |
| `src/Web/Hardened.Web.Testing/Hosts` | the host seam: `ITestHost`, `TestHostAttribute`, `PipelineHost`, `SocketHost` |
| `src/Web/Hardened.Web.Kestrel.Testing` | `KestrelHostAttribute` and the Kestrel host over `KestrelServerRunner` |
| `src/Web/Hardened.Web.AspNetCore.Testing` | `AspNetCoreHostAttribute`, the host over `WebApplication`, and `IAspNetCoreTestComposition` |
| `src/Requests/Hardened.Requests.Abstract/Responses/ResponseExpectation.cs` | `Match`, `MatchStatus`, the body and header readers every `FromResponse` uses |
| `Hardened.IntegrationTests.WebApp.SUT.Tests/Transport` | `KiotaReturnsTests` and `RefitReturnsTests`, the same statuses through both generators |

The `hardened-web` template scaffolds either half. `--client kiota`, the default, puts
`[assembly: KiotaTesting]` in the test project's `Bootstrap.cs` and `Hardened.Kiota.Testing` in
its csproj; `--client refit` puts `[assembly: RefitTesting]` and `Hardened.Refit.Testing` there,
with a client project that runs Refitter over the exported document under the settings in its
`.refitter` file - every operation returning `IApiResponse<T>`, the models in `<Name>.Client.Models`.
Both write `Hardened1ClientTests.cs` with `Returns`; the Refit one names operations by their
operationId, which is why it differs between the code-first and specification-first contracts.
