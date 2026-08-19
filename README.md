# Hardened.Framework

The core framework for the [Hardened](https://ipjohnson.github.io/Hardened.Docs) ecosystem — a compile-time, source-generated .NET framework for building web APIs, AWS Lambda functions, and canary tests.

Hardened uses C# source generators to wire up dependency injection, request routing, configuration, and more at compile time — eliminating runtime reflection and delivering fast startup, small binaries, and strong type safety.

## Packages

| Package | Description |
|---|---|
| `Hardened.Shared.Runtime` | Core: module entry point (`[HardenedModule]`), configuration, environment, application lifecycle |
| `Hardened.Shared.Testing` | Test framework: `[HardenedTest]`, `[Mock]`, `ITestContext` |
| `Hardened.Requests.Abstract` | Request/response abstractions, execution pipeline interfaces |
| `Hardened.Requests.Runtime` | Execution pipeline implementation, filters |
| `Hardened.Requests.Testing` | Request testing utilities |
| `Hardened.Web.Runtime` | Web routing: `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]`, `[BasePath]` |
| `Hardened.Web.AspNetCore.Runtime` | ASP.NET Core integration bridge |
| `Hardened.Web.Testing` | `ITestWebApp`, `TestWebRequest`, `TestWebResponse` |
| `Hardened.SourceGenerator` | Shared generator source library (not referenced directly) |
| `Hardened.DependencyModules.SourceGenerator` | Module wiring and DI source generator |
| `Hardened.Web.SourceGenerator` | Web routing source generator |
| `Hardened.Library.SourceGenerator` | Library/module source generator |

## Quick Start

```csharp
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.AspNetCore.Runtime;
using Hardened.Web.Runtime.Attributes;

[HardenedModule]
[AspNetCoreRuntime]
public partial class Application { }

public class HelloController {
    [Get("/hello/{name}")]
    public string Hello(string name) {
        return $"Hello, {name}!";
    }
}
```

```csharp
// Program.cs
using Hardened.Shared.Runtime.Application;
using Hardened.Web.AspNetCore.Runtime;

var builder = WebApplication.CreateBuilder(args);

// Registered by the application, not by the framework: only the application knows where its
// environment name and arguments come from. Startup fails on the first service that asks for
// one if this is missing.
builder.Services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(builder.Services);

var app = builder.Build();

app.UseHardened();

app.Run();
```

## Documentation

Full documentation is available at **[ipjohnson.github.io/Hardened.Docs](https://ipjohnson.github.io/Hardened.Docs)**.

- [Getting Started](https://ipjohnson.github.io/Hardened.Docs/guide/getting-started)
- [Modules](https://ipjohnson.github.io/Hardened.Docs/guide/modules)
- [Dependency Injection](https://ipjohnson.github.io/Hardened.Docs/guide/services)
- [Web Routing](https://ipjohnson.github.io/Hardened.Docs/guide/routing)
- [Testing](https://ipjohnson.github.io/Hardened.Docs/guide/testing)
- [OpenAPI](https://ipjohnson.github.io/Hardened.Docs/guide/openapi)

## Related Repositories

- [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) — AWS Lambda runtimes, DynamoDB/SQS clients, CDK support
- [Hardened.Canaries](https://github.com/ipjohnson/Hardened.Canaries) — Canary testing framework
- [Hardened.Docs](https://github.com/ipjohnson/Hardened.Docs) — Documentation site
