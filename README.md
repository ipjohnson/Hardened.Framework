# <picture><source media="(prefers-color-scheme: dark)" srcset="assets/hardened-mark-dark.svg"><img src="assets/hardened-mark.svg" alt="" width="34"></picture> Hardened.Framework

A compile-time, source-generated .NET framework for web APIs and AWS Lambda. The dependency
injection, request routing, parameter binding and configuration are written by source generators
during the build rather than resolved by reflection at startup — what runs is ordinary C# you can
open and read.

Full documentation: **[ipjohnson.github.io/Hardened.Docs](https://ipjohnson.github.io/Hardened.Docs)**

## Start here

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Greeter
cd Greeter
dotnet run --project src/Greeter.Host
```

That is a working API with tests, on <http://localhost:5080>, with a reference page at `/docs`.

```console
$ curl localhost:5080/greeting/world
{"message":"Hello, world!"}
```

Start from a template rather than from bare packages: the runtime packages carry no analyzers, so a
project that references only them compiles to an application that answers 404 to everything. The
templates wire the generators, pin every version in one place, and split the projects so the host
can be swapped without touching the code.

| Template | What you get |
|---|---|
| `hardened-web` | An implementation library, a host, and tests. `--host kestrel\|aspnet\|aws-lambda`, `--contract code\|openapi\|smithy` |
| `hardened-function` | An AWS Lambda function and tests. `--trigger invoke\|sqs` |
| `hardened-library` | A reusable module an application picks up with one attribute |

See the [templates guide](https://ipjohnson.github.io/Hardened.Docs/guide/project-templates) for
every option, and [getting started](https://ipjohnson.github.io/Hardened.Docs/guide/getting-started)
for the same project assembled by hand.

## The contract is yours to choose

Hardened builds the same application from any of three contract styles — pick with
`--contract code|openapi|smithy` on the template, or change your mind later.

### Code-first

The C# is the contract. A route is an attribute on a method of a plain class — no base type, no
interface, no registration — and the OpenAPI document is generated *from* your handlers:

```csharp
[BasePath("/greeting")]
public partial class GreeterLibrary;      // the module

public class GreetingController {
    [Get("/{name}")]
    public Greeting Hello(string name) => new($"Hello, {name}!");
}
```

The application names its runtime and the libraries it composes, and that is the whole bootstrap:

```csharp
[HardenedModule]
[KestrelRuntime]          // or [AspNetCoreRuntime], or [LambdaWebModule] from Hardened.Amz
[GreeterLibrary]
public partial class Application;
```

### OpenAPI-first

An OpenAPI document is the contract. The build generates the models, a service interface per tag,
the routes and the validation its constraints describe — you implement the interface it wrote:

```xml
<ItemGroup>
    <AdditionalFiles Include="contracts/todos.yaml" />
</ItemGroup>
```

There are no route attributes anywhere in the project. Add an operation to the contract and the
build writes the model, the route and the validation, then stops compiling until your service
implements the new method — the specification and the code cannot disagree.

### Smithy-first

The same generated output from a [Smithy](https://smithy.io) model instead of an OpenAPI document.
Needs the Smithy CLI on `PATH`; the build names the version it expects if yours differs.

### Whichever you choose

The application serves its OpenAPI document at `/openapi.json` and a reference page at `/docs` —
generated from the routing table when the code is the contract, your contract embedded verbatim
when the contract came first. Hardened does not generate clients; the document is the deliverable,
and Kiota or NSwag pointed at it does the rest. See
[the OpenAPI document](https://ipjohnson.github.io/Hardened.Docs/guide/openapi-document) and
[generating from OpenAPI](https://ipjohnson.github.io/Hardened.Docs/guide/openapi).

## What else the build writes

The same generate-don't-reflect treatment runs through the rest of the framework:

- **[Parameter binding](https://ipjohnson.github.io/Hardened.Docs/guide/parameter-binding)** —
  path, query, header, body and injected services bind through code emitted for each handler's
  exact signature; a binding that cannot work is a build error.
- **[Configuration](https://ipjohnson.github.io/Hardened.Docs/guide/configuration)** — a
  configuration model is a partial class of private fields; the generator writes the interface,
  the implementation and the environment-variable reads.
- **[Declared responses](https://ipjohnson.github.io/Hardened.Docs/guide/responses)** — a handler
  that can answer more than one way says so in its signature, and the document reflects it.
- **[Authorization](https://ipjohnson.github.io/Hardened.Docs/guide/authorization)** — a handler
  says what it needs; the pipeline decides whether the caller has it.
- **[Streaming responses](https://ipjohnson.github.io/Hardened.Docs/guide/streaming)** — return
  `IAsyncEnumerable<T>` and the response streams.
- **[Content negotiation](https://ipjohnson.github.io/Hardened.Docs/guide/content-negotiation)**,
  **[filters](https://ipjohnson.github.io/Hardened.Docs/guide/execution-pipeline)** and
  **[System.Text.Json configuration](https://ipjohnson.github.io/Hardened.Docs/guide/json)**
  follow the same shape.

Everything lands as readable source: `EmitCompilerGeneratedFiles` is on in the templates, so the
routing table, the handlers and the binding sit under `obj/<configuration>/<tfm>/generated/`.

## Testing

A test method declares the services it wants as parameters and the framework boots the real
application around it, substituting mocks where you ask. The pipeline under test is the pipeline
that ships. See [testing](https://ipjohnson.github.io/Hardened.Docs/guide/testing) and
[testing web apps](https://ipjohnson.github.io/Hardened.Docs/guide/testing-web).

## Packages

Everything ships to nuget.org as `Hardened.*`; the templates reference the right set for each
project shape. One rule matters when assembling by hand: the source generators are not optional and
do not flow transitively — the project that owns the application must reference them directly. The
full list, and which project references what, is in the
[package reference](https://ipjohnson.github.io/Hardened.Docs/reference/packages).

## Related repositories

- [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) — AWS Lambda runtimes, test harnesses, DynamoDB client, CDK constructs
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — the documentation site
