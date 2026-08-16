# Multi-cloud transport — design

**Written:** 2026-08-16
**Against:** `Hardened.Framework` @ `b690c16`, `Hardened.Amz` @ `b9c940a`
**Covers:** Azure Functions and GCP Cloud Run transports, and the one framework change they need.

Read alongside the **Scope** section of `AMZ-FEATURE-REVIEW.md` (set 2026-08-16), which this design
is bound by.

Verification: the codebase findings in "Where ASP.NET actually lives" were read out of every
`*.csproj` in both repos, not recalled. The Azure Functions model lifecycle was checked against
Microsoft's retirement notice rather than assumed.

---

## Constraints

Three, all settled before this document.

**1. Transport only.** Getting a request or an event off a cloud's wire format and into the
execution pipeline is in scope. Wrapping that cloud's services is not — no object storage, queue
publishing, secret/parameter stores, document stores, or idempotency persistence. See the Scope
section of `AMZ-FEATURE-REVIEW.md`.

**2. No ASP.NET on the integration path.** The transports are built natively against each
platform's own types. Routing an integration through `Hardened.Web.AspNetCore.Runtime` because it
is quicker is the thing this constraint exists to prevent — it would put the ASP.NET pipeline back
on exactly the deployment shape the framework is differentiated for.

Kestrel is not in question. `Hardened.Web.Kestrel.Runtime` works at `IHttpApplication` /
`IFeatureCollection`, below the ASP.NET pipeline, and is already proven AOT-clean by
`Hardened.IntegrationTests.Aot.SUT`. It is the Cloud Run host.

**3. No IaC.** Neither cloud gets a CDK equivalent in this pass. Azure has `Azure.Provisioning` if
that changes; GCP has no .NET-native option and would mean emitting Terraform or Pulumi.

---

## Where ASP.NET actually lives

Read out of every `*.csproj` in both repos, 2026-08-16:

| Project | ASP.NET |
|---|---|
| `Hardened.Requests.Abstract` | none — `Microsoft.Extensions.{DI.Abstractions, Logging.Abstractions, Primitives}` |
| `Hardened.Requests.Runtime` | none — same, plus DependencyModules and ValidationModules |
| `Hardened.Shared.Runtime` | none — `Microsoft.Extensions.*` only |
| `Hardened.Web.Runtime` | none — Logging and Primitives only |
| `Hardened.Web.Kestrel.Runtime` | `FrameworkReference Microsoft.AspNetCore.App` |
| `Hardened.Web.AspNetCore.Runtime` | `FrameworkReference Microsoft.AspNetCore.App` |
| **all of `Hardened.Amz` shipping code** | **none** — the only hit in the repo is `Web.Lambda.Harness.Tests` |

So routing, binding, serialization, validation and OpenAPI are already ASP.NET-free, and the
constraint above is about keeping new work that way rather than undoing anything.

The two `FrameworkReference` projects are also different in kind. `Hardened.Web.Kestrel.Runtime`
touches only `Hosting.Server`, `Http.Features`, `Server.Kestrel.Core` and
`Server.Kestrel.Transport.Sockets` — Kestrel as a socket server, no `HttpContext`, no middleware, no
MVC. `Hardened.Web.AspNetCore.Runtime` is the pipeline bridge, and is the one no integration should
route through.

---

## Azure Functions

### Model choice

**Isolated worker.** Not a preference — the in-process model retires **10 November 2026**, after
which Azure stops accepting deployments to it and existing apps stop receiving security and feature
updates. In-process also never supported .NET past 8, which is most of why it is being retired.
There is no version of this design in which in-process was a candidate.

`Microsoft.Azure.Functions.Worker` is a gRPC worker process and is not ASP.NET. The ASP.NET
integration is opt-in through `ConfigureFunctionsWebApplication()`; this design uses
`ConfigureFunctionsWorkerDefaults()` and never references
`Microsoft.Azure.Functions.Worker.Extensions.Http.AspNetCore`.

### HTTP transport

Five pieces, mirroring the API Gateway transport in `Hardened.Amz.Web.Lambda.Runtime`:

| Piece | Wraps | Note |
|---|---|---|
| `HttpRequestDataExecutionRequest : IExecutionRequest` | `HttpRequestData` | `Body` is already a `Stream` |
| `HttpResponseDataExecutionResponse : IExecutionResponse` | `HttpResponseData` | `Body` is a writable `Stream` |
| `FunctionsExecutionContext : IExecutionContext` | both | mirrors `ApiGatewayV2ExecutionContext` |
| `FunctionsEventProcessor` | — | scope → context → chain → default 200 → metrics dispose |
| `IFunctionContextAccessor` | `FunctionContext` | analogue of `ILambdaContextAccessor` |

**This transport is strictly simpler than the AWS one**, and for a structural reason worth stating
so nobody re-adds the complexity by analogy. `ApiGatewayEventProcessor` is ~180 LOC largely because
API Gateway hands Lambda a JSON envelope: the request body arrives as a base64 or UTF-8 *string* and
has to be decoded into a pooled `MemoryStream`, and the response has to be buffered into another
pooled `MemoryStream` and then materialized back to a string at the end. Azure hands over real
streams in both directions. Both the inbound decode and the outbound buffer-and-copy disappear —
write straight through to `res.Body`.

Mapping details to settle up front:

- **Headers.** `HttpRequestData.Headers` is `HttpHeadersCollection`, enumerating as
  `KeyValuePair<string, IEnumerable<string>>`. Flatten to `IDictionary<string, StringValues>`.
- **Query.** Parse off `req.Url.Query` into `IQueryStringCollection`. `Path` is
  `req.Url.AbsolutePath`.
- **Cookies.** `IReadOnlyCollection<IHttpCookie>` inbound; `HttpCookies.Append` outbound. Unlike the
  Lambda transport there is no `Set-Cookie` string to build, so `CookieSetOptions` maps field by
  field onto the cookie object rather than through `AppendSettings`.
- **`Clone(...)`.** Implement the full rebinding contract from the start. Item 4 of
  `AMZ-FEATURE-REVIEW.md` was `Clone()` silently discarding every argument on both API Gateway
  transports; the conformance suite now catches it, and this transport should be held to it from
  its first commit rather than after.
- **`IsBinary`.** Not meaningful here — the body is a stream either way. No base64 branch.

### Entry point

Source-generated, one catch-all function, so Hardened's routing table does the routing rather than
the Functions host:

```csharp
[Function("HardenedHttp")]
public Task<HttpResponseData> Run(
    [HttpTrigger(AuthorizationLevel.Anonymous,
        "get","post","put","delete","patch","head","options",
        Route = "{*path}")] HttpRequestData req,
    FunctionContext ctx) => _processor.Process(req, ctx);
```

Same approach as the API Gateway `$default` route. The generator mirrors
`WebLambdaApplicationBootstrapGenerator`; `Microsoft.Azure.Functions.Worker.Sdk` picks the attribute
up and emits `functions.metadata` at build time.

Per the workspace rule, the generator emits through **CSharpAuthor** — never `StringBuilder`.

**AOT is a goal on this path.** The isolated worker supports `PublishAot` on .NET 8, and with no
ASP.NET in the graph there is nothing here to fight the trimmer. The Azure equivalent of
`Hardened.IntegrationTests.Aot.SUT` should exist before the transport is called done, for the same
reason the Kestrel one does: a warning-free trim analysis says the framework contains nothing the
trimmer cannot follow, and says nothing about whether ILC produces a binary that serves a request.

### Event sources

None of these touch ASP.NET. Each is a `[HardenedModule]` implementing
`IServiceCollectionConfiguration` plus a `BaseBatchExecutionFilter<TEvent, TRecord>` subclass and a
`.Testing` package — the seam already proven twice in Amz, where SQS and DynamoDB Streams are ~250
lines each including tests.

| Source | Binding | Notes |
|---|---|---|
| Service Bus | `ServiceBusReceivedMessage[]` + `ServiceBusMessageActions` | settles per message, not by returning failed ids |
| Storage Queue | `QueueMessage` | same shape, simpler |
| Timer | `[TimerTrigger]` | no batch filter needed |
| Cosmos change feed | `IReadOnlyList<T>` | see divergence below |

**Cosmos does not port cleanly from DynamoDB Streams.** The change feed delivers the *current*
document only — there is no before-image short of the full-fidelity change feed. Amz's
`[OldImage]` / `[NewImage]` attribute pair therefore has no Cosmos equivalent, and Cosmos handlers
get one binding rather than two. Do not paper over this with a null `[OldImage]`; a handler written
against the AWS pair would compile and silently misbehave.

---

## GCP

### Target: Cloud Run, not Cloud Functions

Cloud Run is a container listening on `$PORT`. `Hardened.Web.Kestrel.Runtime` already is that, so
**there is no new HTTP transport to write.** What is needed is packaging:

- container template and Dockerfile
- `$PORT` binding in the Kestrel host configuration
- SIGTERM handling for graceful drain
- startup-probe-compatible boot

Cloud Functions is explicitly out: Google's .NET Functions Framework is an ASP.NET Core application,
so targeting it would violate constraint 2 for no gain. Cloud Functions gen2 runs on Cloud Run
underneath in any case.

### Event delivery — the piece worth building well

Eventarc, Pub/Sub push and GCS notifications all arrive as HTTP POSTs carrying **CloudEvents**.

`CloudEventExecutionFilter` sits in the pipeline, detects a CloudEvent and parses the envelope in
both bindings:

- **binary mode** — `ce-specversion`, `ce-type`, `ce-source`, `ce-id` headers, payload as the body
- **structured mode** — `application/cloudevents+json`, whole envelope in the body

This is the highest-leverage item in the plan because CloudEvents is a CNCF spec, not a Google one.
One implementation serves Eventarc, Pub/Sub push, GCS notifications, **Azure Event Grid**, and **AWS
EventBridge API destinations**. Write it once, use it on three clouds.

Pub/Sub push layers on top: `message.data` is base64, alongside `message.attributes`,
`message.messageId`, `message.publishTime` and a `subscription` field. Ack is any 2xx, nack is
anything else. **One message per request** — there is no batch on the push path and therefore no
partial-failure story to design.

Pub/Sub *pull* is the batched shape and does fit `BaseBatchExecutionFilter`, but it is a different
deployment — a long-running worker rather than a request handler — and is deferred.

---

## The one framework change

It is a **move**, not an edit in place. `BaseBatchExecutionFilter` and
`IBatchProcessorExceptionHandler` live in
`Hardened.Amz/src/Lambda/Function/Hardened.Amz.Function.Lambda.Runtime/Filter/` — there is no batch
filter anywhere in `Hardened.Framework` today. So the batching abstraction is currently AWS-owned,
and every cloud after the first would either take a dependency on the AWS package or copy the class.

The change is to lift both types into the framework alongside the rest of the request pipeline, and
in the same pass give the base filter an `IBatchFailureReporter` seam. Four platforms express
partial batch failure four different ways:

| Platform | Mechanism |
|---|---|
| SQS | return the failed message ids |
| Service Bus | settle each message individually via `ServiceBusMessageActions` |
| Pub/Sub push | no batch — the HTTP status is the ack |
| Kinesis | checkpoint a sequence number |

This needs an `IBatchFailureReporter` seam behind the filter **before the second cloud lands**. If
it goes in after, the SQS shape gets copy-pasted per platform and diverges — which is how items 5
and 6 of `AMZ-FEATURE-REVIEW.md` happened in the first place.

That is the only core change. Routing, binding, serialization, validation and OpenAPI all sit above
`IExecutionRequest` and are untouched by either cloud.

---

## Effort

Excludes IaC and anything struck by the scope constraint.

| | Work | Estimate |
|---|---|---|
| **Framework** | lift `BaseBatchExecutionFilter` out of Amz + `IBatchFailureReporter` seam | ~4 days |
| **Azure** | HTTP transport | ~1 week |
| | entry-point source generator | ~1 week |
| | one event source (Service Bus first) | ~3 days |
| | testing harness + AOT SUT | ~4 days |
| | **subtotal** | **~3 weeks** |
| **GCP** | Cloud Run packaging and template | ~2 days |
| | `CloudEventExecutionFilter` | ~1 week |
| | **subtotal** | **~1.5 weeks** |

Ordering: the framework seam first, then GCP, then Azure. GCP ahead of Azure because Cloud Run needs
no new transport, so it reaches a working second cloud fastest and forces the CloudEvents filter out
early — which Azure Event Grid then reuses.

---

## Open questions

1. **Does the Azure transport get its own repo, or live in a `Hardened.Azr` sibling?** The Amz
   layout (runtime / testing / source generator / integration app per event source) ports directly
   either way. Assumed: a sibling repo matching `Hardened.Amz`, since the packaging, CI and release
   line are per-repo in this workspace.
2. **Does the Cosmos handler shape need a framework-level answer**, or is one binding simply the
   Cosmos story? Assumed the latter — the divergence is real and hiding it would be worse.
3. **Kestrel host configuration for Cloud Run** — whether `$PORT` binding belongs in
   `Hardened.Web.Kestrel.Runtime` as a general environment-driven default, or in a GCP-specific
   packaging layer. Leaning general: reading a port from the environment is not GCP-specific and
   Container Apps wants the same thing.
