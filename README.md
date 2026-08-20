# Hardened.Framework

The core framework for the [Hardened](https://ipjohnson.github.io/Hardened.Docs) ecosystem — a compile-time, source-generated .NET framework for building web APIs and AWS Lambda functions.

Hardened uses C# source generators to write dependency injection, request routing, parameter binding and configuration during the build, rather than resolving them by reflection at startup. What runs is ordinary C# you can open and read.

## Start here

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Greeter
cd Greeter
dotnet run --project src/Greeter.Host
```

That is a working API with tests, on <http://localhost:5080>. It opens a reference page at `/docs` on first run.

```console
$ curl localhost:5080/greeting/world
{"message":"Hello, world!"}
```

The templates exist because the packages alone are easy to get wrong: the runtime packages carry no analyzers, so a project that references only them compiles to an application that answers 404 to everything. The templates wire the generators, pin every version in one place, and split the projects so the host can be swapped without touching the code.

| Template | What you get |
|---|---|
| `hardened-web` | An implementation library, a host, and tests. `--host kestrel\|aspnet\|aws-lambda`, `--contract code\|openapi\|smithy` |
| `hardened-function` | An AWS Lambda function and tests. `--trigger invoke\|sqs` |
| `hardened-library` | A reusable module an application picks up with one attribute |

Every combination builds, tests and serves before a release ships — that is what `scripts/verify-templates.sh` is.

See the [templates guide](https://ipjohnson.github.io/Hardened.Docs/guide/project-templates) for the full set of options, and [getting started](https://ipjohnson.github.io/Hardened.Docs/guide/getting-started) for the same project assembled by hand.

## What the generated code looks like

A route is an attribute on a method of a plain class. No base type, no interface, no registration:

```csharp
[BasePath("/greeting")]
public partial class GreeterLibrary;      // the module

public class GreetingController {
    [Get("/{name}")]
    public Greeting Hello(string name) => new($"Hello, {name}!");
}
```

The application names its runtime and the libraries it composes:

```csharp
[HardenedModule]
[KestrelRuntime]          // or [AspNetCoreRuntime], or [LambdaWebModule] from Hardened.Amz
[GreeterLibrary]
public partial class Application;
```

`EmitCompilerGeneratedFiles` is on in the templates, so the routing table, the handler for each route and the parameter binding are all readable under `obj/<configuration>/<tfm>/generated/`.

## Packages

All published to nuget.org. The templates reference the right ones for you; this table is for assembling a project by hand.

**Runtime**

| Package | Description |
|---|---|
| `Hardened.Shared.Runtime` | Core: `[HardenedModule]`, configuration, environment, application lifecycle |
| `Hardened.Requests.Abstract` | Request/response abstractions, execution pipeline interfaces |
| `Hardened.Requests.Runtime` | Execution pipeline implementation, filters |
| `Hardened.Web.Runtime` | Web routing: `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]`, `[BasePath]` |
| `Hardened.Web.Kestrel.Runtime` | Kestrel host, without the ASP.NET Core request pipeline. The default |
| `Hardened.Web.AspNetCore.Runtime` | ASP.NET Core host, for its middleware, authentication and hosting diagnostics |
| `Hardened.Web.StaticContent` | Static file serving and content compression |
| `Hardened.Requests.Serializers.Newtonsoft` | Newtonsoft.Json serialization, instead of System.Text.Json |

**Source generators.** Not optional, and not transitive — analyzers do not flow through a package reference, so these are referenced by the project that needs them.

| Package | Description |
|---|---|
| `Hardened.Library.SourceGenerator` | Module wiring and DI. Without it `PopulateServiceCollection` does not exist |
| `Hardened.Web.SourceGenerator` | Routing table and handlers from route attributes |
| `Hardened.Function.SourceGenerator` | Handlers from `[HardenedFunction]` |
| `Hardened.Validation.SourceGenerator` | Validators from constraint attributes |
| `Hardened.OpenApi.SourceGenerator` | Front end for an OpenAPI document as the contract |
| `Hardened.Smithy.SourceGenerator` | Front end for a Smithy model as the contract |
| `Hardened.Idl.SourceGenerator` | Shared back end for both contract front ends |
| `Hardened.SourceGenerator` | Shared generator source library, not referenced directly |

**Testing**

| Package | Description |
|---|---|
| `Hardened.Shared.Testing` | `[HardenedTest]`, `[Mock]`, `ITestContext`. xUnit v3 |
| `Hardened.Web.Testing` | `ITestWebApp`, `TestWebRequest`, `TestWebResponse` |
| `Hardened.Requests.Testing` | Request pipeline testing utilities |
| `Hardened.SourceGeneration.Testing` | Harness for testing source generators |

**Templates and views**

| Package | Description |
|---|---|
| `Hardened.Templates` | The `dotnet new` templates above |
| `Hardened.Templates.RazorBlade` | RazorBlade view rendering |

## Documentation

Full documentation is at **[ipjohnson.github.io/Hardened.Docs](https://ipjohnson.github.io/Hardened.Docs)**.

- [Project templates](https://ipjohnson.github.io/Hardened.Docs/guide/project-templates) — the fastest correct start
- [Getting started](https://ipjohnson.github.io/Hardened.Docs/guide/getting-started)
- [Modules](https://ipjohnson.github.io/Hardened.Docs/guide/modules)
- [Registering services](https://ipjohnson.github.io/Hardened.Docs/guide/services)
- [Routing](https://ipjohnson.github.io/Hardened.Docs/guide/routing)
- [Testing](https://ipjohnson.github.io/Hardened.Docs/guide/testing)
- [OpenAPI](https://ipjohnson.github.io/Hardened.Docs/guide/openapi)

## Related Repositories

- [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) — AWS Lambda runtimes, DynamoDB/SQS clients, CDK support
- [Hardened.Canaries](https://github.com/ipjohnson/Hardened.Canaries) — Canary testing framework
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — Documentation site
