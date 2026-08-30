---
layout: home

hero:
  name: Hardened
  text: The wiring is written during the build
  tagline: >-
    Mark a method with a route or a function name, and a source generator emits the routing table,
    the parameter binding and the registration code before the assembly is written.
  image:
    src: /hero.svg
    alt: Attributed handlers on the left becoming a generated route table on the right
  actions:
    - theme: brand
      text: Get started
      link: /guide/getting-started
    - theme: alt
      text: Project templates
      link: /guide/project-templates
    - theme: alt
      text: AWS Lambda
      link: /aws/
    - theme: alt
      text: View on GitHub
      link: https://github.com/ipjohnson/Hardened.Framework

features:
  - title: Handlers, not controllers
    details: >-
      A route is an attribute on a method of a plain class. No base class, no IActionResult, no
      AddControllers(). The generator reads the attribute and emits the handler that calls your
      method.
    link: /guide/routing
    linkText: Routing

  - title: Binding decided at compile time
    details: >-
      Path tokens, query strings, headers, the body and injected services each bind through code
      emitted for that one handler's exact signature. A binding that cannot work is a build error.
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
      interface, the implementation and the environment variable reads.
    link: /guide/configuration
    linkText: Configuration

  - title: Tests that boot the real application
    details: >-
      A test method declares the services it wants as parameters and the framework builds the real
      application around it, substituting mocks where you ask for them.
    link: /guide/testing
    linkText: Testing
---

<div class="hd-sample">

## A handler and an application

Attribute the method. The routing table, the parameter binding and the registration code are
emitted during the build.

```csharp
[HardenedModule]
[KestrelRuntime]
public partial class Application;

public class GreetingController {
    [Get("/hello/{name}")]
    public string Hello(string name) => $"Hello, {name}!";
}
```

```csharp
// Program.cs
var services = new ServiceCollection();

services.AddLogging(logging => logging.AddSimpleConsole(options => options.SingleLine = true));
services.AddHardenedEnvironment(args);

new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenAnyIP(5080));

await app.RunAsync();
```

Or skip the wiring — `dotnet new hardened-web` writes all of it, with tests:

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Greeter
```

Set `EmitCompilerGeneratedFiles` in the project file and the routing table, the handler and the
binding code are all under `obj/` as ordinary C#.

## Two repositories, one framework

The core framework and the AWS integrations version and ship separately, so an application only
carries what it uses.

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
