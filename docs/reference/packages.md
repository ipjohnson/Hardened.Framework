# Packages

Every published package, by repository. See [Installation](/guide/getting-started#the-package-feed)
for the feed configuration — packages are on GitHub Packages, not nuget.org.

Source generator packages are referenced as analysers:

```xml
<PackageReference Include="Hardened.Web.SourceGenerator" Version="..."
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Hardened.Framework

[github.com/ipjohnson/Hardened.Framework](https://github.com/ipjohnson/Hardened.Framework)

### Core

| Package | Contents |
|---|---|
| `Hardened.Shared.Runtime` | Module entry points, configuration binding, environment, application lifecycle, metrics |
| `Hardened.Shared.Testing` | `[HardenedTest]`, `[Mock]`, `ITestContext`, the retry engine |

### Requests

| Package | Contents |
|---|---|
| `Hardened.Requests.Abstract` | `IExecutionContext`, `IExecutionRequest`, `IExecutionResponse`, `IExecutionFilter` |
| `Hardened.Requests.Runtime` | The pipeline: filters, serialisation, validation, error handling |
| `Hardened.Requests.Testing` | Test doubles, and the transport conformance suite every `IExecutionRequest` is held to |
| `Hardened.Requests.Serializers.Newtonsoft` | A Newtonsoft.Json serialiser, for payloads `System.Text.Json` cannot round-trip |

### Web

| Package | Contents |
|---|---|
| `Hardened.Web.Runtime` | Routing, static content, CORS |
| `Hardened.Web.AspNetCore.Runtime` | `[AspNetCoreRuntime]` and `app.UseHardened()` |
| `Hardened.Web.Testing` | `ITestWebApp`, `TestWebRequest`, `TestWebResponse` |

### Templates

| Package | Contents |
|---|---|
| `Hardened.Templates.RazorBlade` | `HardenedRazorTemplates`, `HardenedHtmlTemplate<T>`. Renders `.cshtml` with no ASP.NET Core dependency |

`IHardenedResponseOutput<T>` — what a view implements — and the `[TemplateBase]` /
`[TemplateContentType]` vocabulary a rendering engine's marker declares both live in
`Hardened.Requests.Abstract`, so nothing depends on RazorBlade to name a view or to ship another
engine.

### Console

| Package | Contents |
|---|---|
| `Hardened.Commands` | `[Command]`, `[Option]`, `ICommandHandler<T>`, the parser and help printer |

### Source generators

| Package | Emits |
|---|---|
| `Hardened.DependencyModules.SourceGenerator` | Module wiring for `[HardenedModule]`, including `PopulateServiceCollection` |
| `Hardened.Library.SourceGenerator` | `CreateServiceProvider` and the configuration implementations |
| `Hardened.Web.SourceGenerator` | Route tables and request handlers for `[Get]`, `[Post]`, `[Put]` |
| `Hardened.Function.SourceGenerator` | Function handlers for `[HardenedFunction]` |
| `Hardened.Console.SourceGenerator` | Console entry points and command definitions |
| `Hardened.OpenApi.SourceGenerator` | Models, service interfaces, handlers, routes and validation from an OpenAPI document |
| `Hardened.SourceGenerator` | The shared generator library the others build on. Not referenced directly |

## Hardened.Amz

[github.com/ipjohnson/Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz)

### Lambda runtimes

| Package | Contents |
|---|---|
| `Hardened.Amz.Shared.Lambda.Runtime` | Bootstrap, structured logging, embedded CloudWatch metrics, stage and region types |
| `Hardened.Amz.Function.Lambda.Runtime` | Function invocation and the batch execution filter base |
| `Hardened.Amz.Function.Lambda.Streaming` | Response streaming for function handlers |
| `Hardened.Amz.Web.Lambda.Runtime` | API Gateway proxy events onto the pipeline |
| `Hardened.Amz.Web.Lambda.Streaming` | Response streaming for web applications |
| `Hardened.Amz.Web.Lambda.Harness` | Runs a Lambda web application behind a local HTTP listener |
| `Hardened.Amz.Function.DDB.Runtime` | DynamoDB Streams, with `[NewImage]` and `[OldImage]` |
| `Hardened.Amz.Function.Sqs.Runtime` | SQS batches, with partial batch responses |

### Lambda testing

| Package | Contents |
|---|---|
| `Hardened.Amz.Shared.Lambda.Testing` | `TestLambdaContext` and shared harness pieces |
| `Hardened.Amz.Function.Lambda.Testing` | `[LambdaFunctionTesting]`, `LambdaTestApp` |
| `Hardened.Amz.Function.DDB.Testing` | `TestDynamoDbStream` |
| `Hardened.Amz.Function.Sqs.Testing` | `TestSqsApp` |

### Clients and infrastructure

| Package | Contents |
|---|---|
| `Hardened.Amz.DynamoDbClient` | `IDynamoDbClientProvider`, `DynamoDbOptions`, `[DynamoDbModule]` |
| `Hardened.Amz.DynamoDbClient.Testing` | `[LocalDynamoDb]`, `LocalDynamoDb` — DynamoDB Local in a container |
| `Hardened.Amz.Cdk` | CDK constructs, stage and region types, the deploy command |

### Source generators

| Package | Emits |
|---|---|
| `Hardened.Amz.Function.Lambda.SourceGenerator` | Lambda bootstrap and handler wiring |
| `Hardened.Amz.Web.Lambda.SourceGenerator` | API Gateway entry points and routing |

## Versioning

The two repositories version independently:

| Repository | Released | Continuous feed |
|---|---|---|
| Hardened.Framework | `{line}-rc1000`, from a `v*` tag | `{line}-preview{build}` on every push to main |
| Hardened.Amz | `1.0.{build}` | `1.0.{build}` |

Releases go to nuget.org; the continuous feed is
[GitHub Packages](https://nuget.pkg.github.com/ipjohnson/index.json). Under one line, `preview`
sorts below `rc`, so a preview never shadows the release it precedes.

Pin exact versions across a solution. Mixing framework builds within one application is not a
supported combination — the generated code and the runtime it targets ship together.

A floating pin is the exception worth avoiding. `{line}` names a release line rather than being
open-ended, so `1.0.0-preview*` stopped matching anything the day the line moved — and a float that
matches nothing new does not fail, it silently keeps resolving whatever it last found. Hardened.Amz
tracked a framework three release lines old that way, with a green build throughout.
