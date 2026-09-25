# Packages

The Hardened release publishes 66 packages. The tables on this page list every one of them.

A Kestrel application references these packages, including three source generators:

```xml
<ItemGroup>
  <PackageReference Include="Hardened.Shared.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Web.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Web.Kestrel.Runtime" Version="0.0.0-HARDENED-VERSION" />
  <PackageReference Include="Hardened.Library.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
  <PackageReference Include="Hardened.Web.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
  <PackageReference Include="Hardened.Validation.SourceGenerator" Version="0.0.0-HARDENED-VERSION" PrivateAssets="all" />
</ItemGroup>
```

Every package is on nuget.org. A release publishes all 66 packages at one version.
[Project templates](/guide/project-templates) reference the packages that each project needs.

## Referencing a source generator

Reference a source generator package with `PrivateAssets="all"`, as the example does. The eight
generator packages are `Hardened.Library.SourceGenerator`, `Hardened.Web.SourceGenerator`,
`Hardened.Validation.SourceGenerator`, `Hardened.Function.SourceGenerator`,
`Hardened.OpenApi.SourceGenerator`, `Hardened.Smithy.SourceGenerator`,
`Hardened.Azure.Functions.SourceGenerator` and `Hardened.Gcp.Functions.SourceGenerator`. The
Generator column of the tables below marks each one.

`PrivateAssets` decides which projects run the generator:

| Reference | The generator runs in |
|---|---|
| `PrivateAssets="all"` | The project that declares the reference |
| No `PrivateAssets` | That project, and every project that references it through a `ProjectReference` |

Without `PrivateAssets="all"`, a library's generators also run in a test project that references the
library.

Each project that declares a `[HardenedModule]` or handlers references the generators it needs. A
host project with no `Hardened.Library.SourceGenerator` reference of its own fails with `CS1061`
when the library it references declares that package with `PrivateAssets="all"`:

```text
'Application' does not contain a definition for 'PopulateServiceCollection'
```

`Hardened.OpenApi.SourceGenerator` and `Hardened.Smithy.SourceGenerator` bring
`Hardened.Idl.SourceGenerator` with them. When a library references either one without
`PrivateAssets="all"`, a host project that references the library and `Hardened.Web.SourceGenerator`
fails with `HRDR008`. The build also reports `CS0102` and `CS0111` on the generated routing names.
[Diagnostics](/reference/diagnostics) covers `HRDR008`.

## Core

| Package | Serves | Attribute | Page |
|---|---|---|---|
| `Hardened.Shared.Runtime` | `[HardenedModule]`, `[Enable<T>]`, configuration models and the environment | `[HardenedModule]` | [Modules](/guide/modules) |
| `Hardened.Shared.Testing` | `[HardenedTestEntryPoint]` and `ITestContext`, for a test project. A runner package goes beside it: `DependencyModules.xUnit`, `DependencyModules.xUnit4` or `DependencyModules.NUnit`, which holds `[ModuleTest]` | `[HardenedTestEntryPoint]` | [Writing a test](/guide/testing) |

## Request pipeline

| Package | Serves | Attribute | Page |
|---|---|---|---|
| `Hardened.Requests.Abstract` | The pipeline interfaces `IExecutionContext`, `IExecutionRequest`, `IExecutionResponse` and `IExecutionFilter`, `Response<T1, T2>`, `[HardenedFunction]` and `BatchFailureMode` | | [The execution pipeline](/guide/execution-pipeline) |
| `Hardened.Requests.Runtime` | The pipeline: filters, serialization, validation and error handling, and `[RequireAuthorization]`, `[CacheResponse<T>]`, `[RateLimit]`, `[Timeout]` and `[Retry]` | | [The execution pipeline](/guide/execution-pipeline) |
| `Hardened.Requests.Testing` | Test doubles for the pipeline, and the conformance tests for an `IExecutionRequest` implementation. `Hardened.Web.Testing` and `Hardened.Functions.Testing` bring it | | None |
| `Hardened.Requests.Caching.Memory` | An in-process store for `[CacheResponse<T>]` | `[HardenedMemoryResponseCache]` | [Response caching](/guide/response-caching) |
| `Hardened.Requests.Serializers.MessagePack` | A MessagePack reader and writer | `[MessagePackSerializerLibrary]` | [MessagePack](/guide/message-pack) |
| `Hardened.Requests.Serializers.Newtonsoft` | A Newtonsoft.Json serializer for requests and responses | None. See [Limits](#limits) | None |

## Web

| Package | Serves | Attribute | Page |
|---|---|---|---|
| `Hardened.Web.Runtime` | Routing and the route attributes such as `[Get]` and `[BasePath]`, the OpenAPI document, CORS, compression, conditional requests and the health endpoints | `[HardenedWebModule]` | [Routing](/guide/routing) |
| `Hardened.Web.Kestrel.Runtime` | The Kestrel host, without the ASP.NET Core request pipeline: `HardenedKestrelApplication` | `[KestrelRuntime]` | [Hosts](/guide/hosts) |
| `Hardened.Web.AspNetCore.Runtime` | The ASP.NET Core host: `app.UseHardened()` | `[AspNetCoreRuntime]` | [Hosts](/guide/hosts) |
| `Hardened.Web.StaticContent` | Serves a directory of files as routes | `[HardenedStaticContent]` | None |
| `Hardened.Web.Testing` | `ITestWebApp`, `TestWebRequest`, `TestWebResponse` and the credential attributes, for sending requests in a test | `[WebTesting]` | [Sending requests](/guide/testing-web) |
| `Hardened.Web.Kestrel.Testing` | Runs a test's requests through Kestrel, on a loopback port the kernel picks | `[KestrelTesting]` | [Test hosts](/guide/testing-hosts) |
| `Hardened.Web.AspNetCore.Testing` | Runs a test's requests through the ASP.NET Core pipeline, on a loopback port the kernel picks | `[AspNetCoreTesting]` | [Test hosts](/guide/testing-hosts) |

## Client testing

| Package | Serves | Attribute | Page |
|---|---|---|---|
| `Hardened.Kiota.Testing` | Makes each Kiota client a test parameter that sends its requests into the application | `[KiotaTesting]` | [Typed clients](/guide/testing-clients) |
| `Hardened.Refit.Testing` | Makes each Refit interface a test parameter that sends its requests into the application | `[RefitTesting]` | [Typed clients](/guide/testing-clients) |

## Views and project templates

| Package | Serves | Attribute | Page |
|---|---|---|---|
| `Hardened.Templates.RazorBlade` | Views written as `.cshtml` files and compiled by RazorBlade. The project also references the `RazorBlade` package | `[Enable<RazorTemplates>]` | [Views](/guide/views) |
| `Hardened.Templates` | The `dotnet new` templates `hardened-web`, `hardened-function` and `hardened-library`. `dotnet new install Hardened.Templates` installs it, and no project references it | | [Project templates](/guide/project-templates) |

## Source generators

| Package | Serves | Build item or property | Generator | Page |
|---|---|---|---|---|
| `Hardened.Library.SourceGenerator` | Each module's generated half: `PopulateServiceCollection`, the module's attribute, `CreateServiceProvider` and the configuration. It holds the `Hardened.DependencyModules.SourceGenerator` generator as well | | Yes | [From scratch](/guide/from-scratch) |
| `Hardened.Web.SourceGenerator` | The routing table, a handler class for each route, the typed links and the OpenAPI document | | Yes | [From scratch](/guide/from-scratch) |
| `Hardened.Validation.SourceGenerator` | Validators for constraint attributes | | Yes | [Validation](/guide/validation) |
| `Hardened.Function.SourceGenerator` | Handlers for `[HardenedFunction]` and the trigger attributes | | Yes | [Triggers](/guide/triggers) |
| `Hardened.OpenApi.SourceGenerator` | Models, the service interface, handlers and routes from an OpenAPI document named by a `HardenedOpenApiSpec` item | `HardenedOpenApiSpec` item | Yes | [Generating from OpenAPI](/guide/openapi) |
| `Hardened.Smithy.SourceGenerator` | The same from a Smithy model named by a `HardenedSmithyModel` item. The build needs the Smithy CLI | `HardenedSmithyModel` item | Yes | [Generating from Smithy](/guide/smithy) |
| `Hardened.Idl.SourceGenerator` | The generator the two packages above bring with them. A project does not reference it | | Brought by the two above | [Generating from OpenAPI](/guide/openapi) |
| `Hardened.SourceGenerator` | The generators' shared source. A project that sets `PackageHardenedIncludeSource` to `true` compiles it in. An application does not reference it | `PackageHardenedIncludeSource` | | None |
| `Hardened.SourceGeneration.Testing` | `GeneratorTestHarness`, which runs a generator over an in-memory compilation in a test | | | None |

## Functions

| Package | Serves | Attribute | Page |
|---|---|---|---|
| `Hardened.Functions.Runtime` | The trigger attributes `[Queue]`, `[Topic]`, `[Timer]`, `[Event]`, `[Change]`, `[Stream]` and `[Blob]` | The seven trigger attributes | [Triggers](/guide/triggers) |
| `Hardened.Functions.Testing` | `ITriggerDelivery`, the generated trigger façades a test sends through, and `[PipelineDelivery]` | `[FunctionTesting]` | [Testing functions](/guide/testing-functions) |
| `Hardened.CloudEvents` | The CloudEvents 1.0 reader that the Eventarc, Cloud Storage, Firestore and Event Grid adapters use. They bring it | | None |

## Cloud adapters

Each adapter package sets an MSBuild property to its module's full type name. For example,
`Hardened.Aws.Lambda.Sqs` sets `HardenedQueueModule` to `Hardened.Aws.Lambda.Sqs.SqsModule`. The
build uses the property to register the module. [Triggers](/guide/triggers) covers these
properties. It also covers the cases where an application applies the module attribute itself.

Each adapter's module is in a namespace with the same name as its package. Each adapter's module
brings its cloud's runtime module: `LambdaRuntimeModule` on AWS, `CloudRunRuntime` on Google Cloud
and `FunctionsRuntimeModule` on Azure.

## AWS

`Hardened.Aws.DynamoDbClient` is the DynamoDB client. `Hardened.Aws.Lambda.DynamoDb` is the DynamoDB
Streams adapter.

| Package | Serves | Attribute and build property | Page |
|---|---|---|---|
| `Hardened.Aws.Lambda.Runtime` | The Lambda host: `HardenedLambdaBootstrap`, the invocation loop, and `LambdaEmulator` for running locally | None. Each adapter's module brings `LambdaRuntimeModule` | AWS [Overview](/aws/) |
| `Hardened.Aws.Lambda.Http` | `[Get]`, `[Post]`, `[Put]`, `[Patch]` and `[Delete]`, behind an API Gateway HTTP API or a function URL | `[LambdaHttpModule]`, `HardenedHttpModule` | AWS [Web applications](/aws/lambda-web) |
| `Hardened.Aws.Lambda.Invoke` | `[HardenedFunction]`, invoked directly | `[InvokeModule]`, `HardenedInvokeModule` | AWS [Invocations](/aws/invoke) |
| `Hardened.Aws.Lambda.Sqs` | `[Queue]`, from Amazon SQS | `[SqsModule]`, `HardenedQueueModule` | AWS [Queues](/aws/queue) |
| `Hardened.Aws.Lambda.Sns` | `[Topic]`, from Amazon SNS | `[SnsModule]`, `HardenedTopicModule` | AWS [Topics](/aws/topic) |
| `Hardened.Aws.Lambda.EventBridge` | `[Timer]` and `[Event]`, from Amazon EventBridge | `[EventBridgeModule]`, `HardenedTimerModule` and `HardenedEventModule` | AWS [Timers](/aws/timer) and [Events](/aws/event) |
| `Hardened.Aws.Lambda.DynamoDb` | `[Change]`, from DynamoDB Streams, with `[NewImage]` and `[OldImage]` | `[DynamoDbStreamsModule]`, `HardenedChangeModule` | AWS [Changes](/aws/change) |
| `Hardened.Aws.Lambda.Kinesis` | `[Stream]`, from Kinesis Data Streams | `[KinesisModule]`, `HardenedStreamModule` | AWS [Streams](/aws/stream) |
| `Hardened.Aws.Lambda.S3` | `[Blob]`, from S3 object notifications | `[S3Module]`, `HardenedBlobModule` | AWS [Blobs](/aws/blob) |
| `Hardened.Aws.Lambda` | The runtime and the eight adapters above, in one reference | Every attribute and property above | AWS [Overview](/aws/) |
| `Hardened.Aws.Lambda.Testing` | Delivers a test's messages through the envelope each source sends and the invocation loop, and runs web requests through API Gateway | `[LambdaTesting]`, `[LambdaWebTesting]` | AWS [Testing](/aws/testing) |
| `Hardened.Aws.DynamoDbClient` | `IDynamoDbClientProvider`: named DynamoDB clients | `[DynamoDbClientModule]` | AWS [DynamoDB client](/aws/dynamodb) |
| `Hardened.Aws.DynamoDbClient.Testing` | DynamoDB Local in a Testcontainers container, for a test | `[LocalDynamoDb]` | AWS [DynamoDB client](/aws/dynamodb) |

## Google Cloud

| Package | Serves | Attribute and build property | Generator | Page |
|---|---|---|---|---|
| `Hardened.Gcp.CloudRun.Runtime` | The Cloud Run host, on Kestrel: `CloudRunHost`, `TriggerFrontDoor` and `CloudRunTriggerRequest` | `[CloudRunRuntime]`, `HardenedHttpModule` | | Google Cloud [Overview](/gcp/) |
| `Hardened.Gcp.CloudRun.PubSub` | `[Queue]`, from a Pub/Sub push subscription, and `[Topic]`, from an Eventarc trigger on a Pub/Sub topic | `[PubSubModule]`, `HardenedQueueModule` and `HardenedTopicModule` | | Google Cloud [Queues](/gcp/queue) and [Topics](/gcp/topic) |
| `Hardened.Gcp.CloudRun.Scheduler` | `[Timer]`, from a Cloud Scheduler job | `[SchedulerModule]`, `HardenedTimerModule` | | Google Cloud [Timers](/gcp/timer) |
| `Hardened.Gcp.CloudRun.Invoke` | `[HardenedFunction]`, invoked by a POST to the service | `[InvokeModule]`, `HardenedInvokeModule` | | Google Cloud [Invocations](/gcp/invoke) |
| `Hardened.Gcp.CloudRun.Storage` | `[Blob]`, from Cloud Storage, through Eventarc or a Pub/Sub notification | `[StorageModule]`, `HardenedBlobModule` | | Google Cloud [Blobs](/gcp/blob) |
| `Hardened.Gcp.CloudRun.Firestore` | `[Change]`, from Firestore through Eventarc, with `[OldValue]` | `[FirestoreModule]`, `HardenedChangeModule` | | Google Cloud [Changes](/gcp/change) |
| `Hardened.Gcp.CloudRun.Eventarc` | `[Event]`, any CloudEvent that Eventarc delivers | `[EventarcModule]`, `HardenedEventModule` | | Google Cloud [Events](/gcp/event) |
| `Hardened.Gcp.CloudRun` | The runtime and the six adapters above, in one reference | Every attribute and property above | | Google Cloud [Overview](/gcp/) |
| `Hardened.Gcp.CloudRun.Testing` | Delivers a test's messages as the push, CloudEvent, Scheduler request or invocation that Cloud Run receives | `[CloudRunTesting]` | | Google Cloud [Testing](/gcp/testing) |
| `Hardened.Gcp.Functions.Runtime` | Runs a `[CloudRunRuntime]` application as a Cloud Functions 2nd gen function: `HardenedFunctionsStartup<TApplication>` and `CloudFunctionHost` | | | Google Cloud [Web services](/gcp/web) |
| `Hardened.Gcp.Functions.SourceGenerator` | Writes the function's entry type | | Yes | Google Cloud [Web services](/gcp/web) |

## Azure

| Package | Serves | Attribute and build property | Generator | Page |
|---|---|---|---|---|
| `Hardened.Azure.Functions.Runtime` | The isolated worker host: `UseHardened<TApplication>` and `FunctionsInvocationHandler`. Every Azure project references it, beside `Microsoft.Azure.Functions.Worker.Sdk` | None. Each adapter's module brings `FunctionsRuntimeModule` | | Azure [Overview](/azure/) |
| `Hardened.Azure.Functions.SourceGenerator` | Writes the functions, the metadata provider that lists them and the executor that runs them | | Yes | Azure [Overview](/azure/) |
| `Hardened.Azure.Functions.ServiceBus` | `[Queue]`, from a Service Bus queue, and `[Topic]`, from a subscription of a Service Bus topic | `[ServiceBusModule]`, `HardenedQueueModule` and `HardenedTopicModule` | | Azure [Queues](/azure/queue) and [Topics](/azure/topic) |
| `Hardened.Azure.Functions.Timer` | `[Timer]`, from a timer trigger | `[TimerModule]`, `HardenedTimerModule` | | Azure [Timers](/azure/timer) |
| `Hardened.Azure.Functions.EventHubs` | `[Stream]`, from Event Hubs | `[EventHubsModule]`, `HardenedStreamModule` | | Azure [Streams](/azure/stream) |
| `Hardened.Azure.Functions.CosmosDb` | `[Change]`, from the Cosmos DB change feed | `[CosmosDbModule]`, `HardenedChangeModule` | | Azure [Changes](/azure/change) |
| `Hardened.Azure.Functions.Blobs` | `[Blob]`, from Blob Storage through Event Grid | `[BlobsModule]`, `HardenedBlobModule` | | Azure [Blobs](/azure/blob) |
| `Hardened.Azure.Functions.EventGrid` | `[Event]`, from Event Grid in the CloudEvents schema | `[EventGridModule]`, `HardenedEventModule` | | Azure [Events](/azure/event) |
| `Hardened.Azure.Functions.Http` | The web routes, behind one anonymous HTTP function | `[HttpModule]`, `HardenedHttpModule` | | Azure [Web applications](/azure/web) |
| `Hardened.Azure.Functions` | The runtime and the seven adapters above, in one reference | Every attribute and property above | | Azure [Overview](/azure/) |
| `Hardened.Azure.Functions.Testing` | Builds the trigger data the worker binds and passes it to the invocation handler | `[AzureFunctionsTesting]`, `[AzureFunctionsWebTesting]` | | Azure [Testing](/azure/testing) |

## Retired packages

Besides the 66 packages above, nuget.org holds 23 retired package ids. The release does not publish
them. The table lists all 23.

| Package | Last version | Current package |
|---|---|---|
| `Hardened.Aws.Lambda.ApiGateway` | 0.33.0-rc1000 | `Hardened.Aws.Lambda.Http`, with `[LambdaHttpModule]` in place of `[ApiGatewayModule]` |
| `Hardened.Amz.DynamoDbClient` | 0.22.0-rc1000 | `Hardened.Aws.DynamoDbClient` |
| `Hardened.Amz.DynamoDbClient.Testing` | 0.22.0-rc1000 | `Hardened.Aws.DynamoDbClient.Testing` |
| `Hardened.Amz.Shared.Lambda.Runtime` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Runtime` |
| `Hardened.Amz.Shared.Lambda.Testing` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Testing` |
| `Hardened.Amz.Web.Lambda.Runtime` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Http` |
| `Hardened.Amz.Web.Lambda.SourceGenerator` | 0.22.0-rc1000 | None. A Lambda web project uses `Hardened.Web.SourceGenerator` |
| `Hardened.Amz.Web.Lambda.Streaming` | 0.18.0-rc1000 | `Hardened.Aws.Lambda.Runtime` |
| `Hardened.Amz.Web.Lambda.Harness` | 0.21.0-rc1000 | `LambdaEmulator`, in `Hardened.Aws.Lambda.Runtime` |
| `Hardened.Amz.Function.Lambda.Runtime` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Invoke` |
| `Hardened.Amz.Function.Lambda.SourceGenerator` | 0.22.0-rc1000 | `Hardened.Function.SourceGenerator` |
| `Hardened.Amz.Function.Lambda.Streaming` | 0.18.0-rc1000 | `Hardened.Aws.Lambda.Runtime` |
| `Hardened.Amz.Function.Lambda.Testing` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Testing` |
| `Hardened.Amz.Function.Sqs.Runtime` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Sqs` |
| `Hardened.Amz.Function.Sqs.Testing` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Testing` |
| `Hardened.Amz.Function.DDB.Runtime` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.DynamoDb` |
| `Hardened.Amz.Function.DDB.Testing` | 0.22.0-rc1000 | `Hardened.Aws.Lambda.Testing` |
| `Hardened.Amz.Cdk` | 0.22.0-rc1000 | None |
| `Hardened.Commands` | 0.4.0-rc1000 | None |
| `Hardened.Console.SourceGenerator` | 0.4.0-rc1000 | None |
| `Hardened.DependencyModules.SourceGenerator` | 0.1.0-rc1 | `Hardened.Library.SourceGenerator`, which holds this generator |
| `Hardened.Shared.Testing.xUnit` | 0.39.0-rc1000 | `DependencyModules.xUnit`, or `DependencyModules.xUnit4` for `xunit.v3` 4.x. `[ModuleTest]` replaces `[HardenedTest]` |
| `Hardened.Shared.Testing.NUnit` | 0.39.0-rc1000 | `DependencyModules.NUnit`. `[ModuleTest]` replaces `[HardenedTest]` |

Moving from a `Hardened.Amz` Lambda adapter to its current package takes more than a new id. The
module attributes differ. For example, `Hardened.Amz.Function.Sqs.Runtime` has `[SqsLambda]`, and
`Hardened.Aws.Lambda.Sqs` has `[SqsModule]`.

The last version of each `Hardened.Amz` package is unlisted on nuget.org.
`Hardened.Aws.Lambda.ApiGateway` 0.33.0-rc1000 and `Hardened.DependencyModules.SourceGenerator`
0.1.0-rc1 are unlisted too. The nuget.org search shows the version before the unlisted one. The
search does not show `Hardened.DependencyModules.SourceGenerator` at all. A restore that names an
unlisted version exactly succeeds.

## Versions

A release is tagged `v{line}-rc1000`. The tag without its `v` is the version of every package. The current release
is `0.0.0-HARDENED-VERSION`. Reference every Hardened package at the same version. The templates set
the version of every Hardened package to `$(HardenedVersion)`.

Each push to `main` whose build passes publishes every package to GitHub Packages as
`{line}-preview{build}`. `{line}` is the next release's line. `{build}` is the workflow run number,
zero-padded to six digits. A preview sorts below the release of its line. The feed is
`https://nuget.pkg.github.com/ipjohnson/index.json`. GitHub Packages asks for a GitHub token to
restore, even for a public package.

GitHub Packages also holds versions `1.0.0-preview10132` to `1.0.0-preview10199` of 18 packages.
`Hardened.Web.Runtime` is one of them. These versions sort above every release. A restore that asks
for a version on neither feed resolves to the lowest version above it.

::: warning
With GitHub Packages configured, a version that is not published yet restores as a `1.0.0-preview`
build. NuGet warns `NU1603`, and the restore succeeds.
:::

## Limits

`Hardened.Requests.Serializers.Newtonsoft` has no module that an application can apply.
`[NewtonsoftSerializerLibrary]` fails with `CS0616`:

```text
'NewtonsoftSerializerLibrary' is not an attribute class
```

## Next

| Page | Covers |
|---|---|
| [Project templates](/guide/project-templates) | The templates and the packages each project references |
| [From scratch](/guide/from-scratch) | An application assembled from packages, and the files the generators write |
| [Triggers](/guide/triggers) | The adapter for each trigger on each cloud, and how the build binds it |
| [Diagnostics](/reference/diagnostics) | `HRDR008` and the other build diagnostics |
| [Repository](/reference/repository) | The repository's folders, and how to build and test it |
