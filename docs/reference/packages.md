# Packages

Every published package, by repository. All of them are on **nuget.org** — no private feed and no
token. [Project templates](/guide/project-templates) reference the right ones for you; this page is
for assembling a project by hand.

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
| `Hardened.Shared.Testing` | `[HardenedTest]`, `[Mock]`, `ITestContext`, the retry engine. xUnit v3 |
| `Hardened.SourceGeneration.Testing` | Harness for testing source generators against a real compilation |

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
| `Hardened.Web.Runtime` | Routing, CORS, the OpenAPI document and reference page |
| `Hardened.Web.Kestrel.Runtime` | `[KestrelRuntime]` and `HardenedKestrelApplication`. Kestrel without the ASP.NET Core request pipeline, and the host to reach for first |
| `Hardened.Web.AspNetCore.Runtime` | `[AspNetCoreRuntime]` and `app.UseHardened()`, when you need ASP.NET Core's middleware, authentication or hosting diagnostics |
| `Hardened.Web.StaticContent` | Static file serving, manifests and content compression. Separate from the runtime so an API that serves none does not carry it |
| `Hardened.Web.Testing` | `ITestWebApp`, `TestWebRequest`, `TestWebResponse` |

### Templates

Two unrelated senses of the word, in two packages.

| Package | Contents |
|---|---|
| `Hardened.Templates` | The `dotnet new` project templates: `hardened-web`, `hardened-function`, `hardened-library`. See [Project templates](/guide/project-templates) |
| `Hardened.Templates.RazorBlade` | View rendering: `HardenedRazorTemplates`, `HardenedHtmlTemplate<T>`. Renders `.cshtml` with no ASP.NET Core dependency. See [Views](/guide/templates) |

`IHardenedResponseOutput<T>` — what a view implements — and the `[TemplateBase]` /
`[TemplateContentType]` vocabulary a rendering engine's marker declares both live in
`Hardened.Requests.Abstract`, so nothing depends on RazorBlade to name a view or to ship another
engine.

### Console

| Package | Contents |
|---|---|
| `Hardened.Commands` | `[Command]`, `[Option]`, `ICommandHandler<T>`, the parser and help printer |
| `Hardened.Console.SourceGenerator` | Console entry points and command definitions |

::: warning Not on the current release line
Both are last published at `0.4.0-rc1000` and are not part of the current release. They will not
have the fixes the packages above carry, and mixing release lines within one application is not a
supported combination.
:::

### Source generators

Analyzers do not flow through a package reference, so a generator has to be referenced by the
project that needs it. Referencing only the runtime packages produces an application that compiles
and answers 404 to everything.

| Package | Emits |
|---|---|
| `Hardened.Library.SourceGenerator` | Module wiring for `[HardenedModule]` — `PopulateServiceCollection`, `CreateServiceProvider` and the configuration implementations. Carries `Hardened.DependencyModules.SourceGenerator` inside it, so that one is not referenced separately |
| `Hardened.Web.SourceGenerator` | Route tables and request handlers for `[Get]`, `[Post]`, `[Put]`, `[Delete]`, `[Patch]` |
| `Hardened.Function.SourceGenerator` | Function handlers for `[HardenedFunction]` |
| `Hardened.Validation.SourceGenerator` | Validators from constraint attributes, and their registration |
| `Hardened.OpenApi.SourceGenerator` | Front end: an OpenAPI document into the normalised model |
| `Hardened.Smithy.SourceGenerator` | Front end: a Smithy model into the normalised model. Needs the Smithy CLI on `PATH` |
| `Hardened.Idl.SourceGenerator` | Back end for both front ends: models, service interfaces, handlers, routes and validation |
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

The one deliberate float is the `Hardened.Amz` version in the Lambda project templates, which is
open-ended (`0.*-*`) rather than tied to a line. The two repositories release in sequence, so for a
short window the framework is ahead of Hardened.Amz — an exact pin there would name a version that
does not exist yet, and every generated Lambda project would fail to restore until the second
release landed. It resolves to the newest published Hardened.Amz, which works because the template
pins the framework packages higher. The template gate prints the version it resolved to, so a float
that stops resolving is visible rather than silent.
