---
layout: home

hero:
  name: Hardened
  text: The wiring is written during the build
  tagline: >-
    Mark a method with a route or a function name, and a source generator emits the routing table,
    the parameter binding and the registration code before the assembly is written. Nothing
    reflects over your types at startup, so what runs is what you can read.
  image:
    src: /hero.svg
    alt: Attributed handlers on the left becoming a generated route table on the right
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: AWS Lambda
      link: /aws/
    - theme: alt
      text: View on GitHub
      link: https://github.com/ipjohnson/Hardened.Framework

features:
  - title: Handlers, not controllers
    details: >-
      A route is an attribute on a method of a plain class. No base class to inherit, no
      IActionResult to wrap a return value in, no AddControllers() to remember. The generator reads
      the attribute and emits the handler that calls your method.
    link: /guide/routing
    linkText: Routing

  - title: Binding decided at compile time
    details: >-
      Path tokens, query strings, headers, the body and injected services each bind through code
      emitted for that one handler's exact signature. A binding that cannot work is a build error,
      not a null argument in production.
    link: /guide/parameter-binding
    linkText: Parameter binding

  - title: The same application on Lambda
    details: >-
      Swap the runtime module and the handlers you already wrote run behind API Gateway, on a
      DynamoDB stream, or over an SQS batch — with partial batch responses and structured
      CloudWatch logging wired in.
    link: /aws/
    linkText: AWS runtimes

  - title: Configuration that names its variables
    details: >-
      A configuration model is a partial class of private fields. The generator writes the
      interface, the implementation and the environment variable reads, so the set of variables an
      application consumes is a list you can produce from the source.
    link: /guide/configuration
    linkText: Configuration

  - title: Tests that boot the real application
    details: >-
      A test method declares the services it wants as parameters and the framework builds the
      application around it, substituting mocks where you ask for them. The pipeline under test is
      the pipeline that ships.
    link: /guide/testing
    linkText: Testing
---

<div class="hd-sample">

## The cost of finding out at run time

A conventional .NET web application decides most of itself after it has started. Controllers are
discovered by scanning assemblies, actions are matched to routes by convention, arguments are bound
by inspecting parameters with reflection, and the container resolves constructor arguments by
looking at types it was handed.

Each of those is a decision the compiler could have made and didn't. So a typo in a route template,
a parameter with no binding source, and a service nobody registered all fail the same way: at run
time, on the first request that happens to reach them, in the environment you deployed to.

## What Hardened does instead

Attribute the method. The rest is emitted during the build.

```csharp
[HardenedModule]
[AspNetCoreRuntime]
public partial class Application { }

public class GreetingController {
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));

new Application().PopulateServiceCollection(builder.Services);

var app = builder.Build();

app.UseHardened();
app.Run();
```

There is no `[ApiController]`, no `ControllerBase`, no `AddControllers()`. Set
`EmitCompilerGeneratedFiles` in the project file and the routing table, the handler and the binding
code are all sitting under `obj/` as ordinary C# — the same code you would have written by hand, and
the ground truth for what the application actually does.

## Two repositories, one framework

The core framework and the AWS integrations version and ship separately, because a web API that
never touches AWS should not carry the AWS SDK.

| Repository | What it holds |
|---|---|
| [Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework) | Modules, DI, configuration, routing, binding, templates, testing |
| [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz) | Lambda runtimes, DynamoDB and SQS clients, CDK constructs |

</div>

<style>
.hd-sample {
  max-width: 1152px;
  margin: 0 auto;
  padding: 0 24px 64px;
}

.hd-sample h2 {
  border-top: 1px solid var(--vp-c-divider);
  padding-top: 40px;
  margin-top: 8px;
}
</style>
