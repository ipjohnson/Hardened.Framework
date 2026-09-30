# Web applications

`[LambdaHttpModule]` serves an application's routes from AWS Lambda. It can sit behind an API Gateway
HTTP API or REST API, a function URL, or an Application Load Balancer. Each request reaches the
function as an invocation that carries an event. An HTTP API and a function URL send API Gateway
payload format 2.0. A REST API and a load balancer send payload format 1.0. The function reads
either format and answers in the format the event came in.

`dotnet new hardened-web -n Todos --host aws-lambda` writes a library with the routes and a host
project for Lambda. The application class in `src/Todos.Host/Application.cs` declares the attribute:

```csharp
using Hardened.Aws.Lambda.Http;
using Hardened.Shared.Runtime.Attributes;

namespace Todos.Host;

[HardenedModule]
[LambdaHttpModule]
[TodosLibrary]
public partial class Application;
```

A request through the [local API Gateway emulator](#running-it-locally) reaches a route in the
library:

```http
GET /todos/1

HTTP/1.1 200 OK
Content-Type: application/json

{"id":1,"title":"Read the generated code","done":true}
```

The adapter turns the event into the request that the routing table matches. It turns the response
into the function's answer. The routes, filters and parameter binding are the ones that the
application has on every host. [Routing](/guide/routing) covers them.

## Packages and registration

The host project references two packages:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.Http" Version="0.0.0-HARDENED-VERSION" />
```

The AWS [Overview](/aws/) lists the other packages that every Lambda project references.

`Hardened.Aws.Lambda.Http` sets the build property `HardenedHttpModule` to
`Hardened.Aws.Lambda.Http.LambdaHttpModule`. When a project compiles `[Get]`, `[Post]`, `[Put]`,
`[Patch]` or `[Delete]` handlers together with its application class, the build registers
`LambdaHttpModule` from that property. The application class then names no module.

When the routes are compiled in a referenced library, as in the template, the application class
declares `[LambdaHttpModule]` itself. Without the attribute, the host project still builds. The
application stops at startup with this exception:

```text
Unhandled exception. System.InvalidOperationException: No service for type 'Hardened.Aws.Lambda.Runtime.Hosting.LambdaInvocationHandler' has been registered.
```

The attribute has no settings. It does not bring `[HardenedWebModule]`. The module that holds the
routes declares that attribute, as the template's `TodosLibrary` does. [Hosts](/guide/hosts) covers
it.

## What a handler receives

| The request's | Comes from the event's |
|---|---|
| Method | `requestContext.http.method` |
| Path | `rawPath`, with the stage removed |
| Query string | `queryStringParameters` |
| Headers | `headers` |
| Cookies | `cookies` |
| Body | `body`, decoded from base64 when `isBase64Encoded` is true |

When `rawPath` starts with `/` and the stage's name, the adapter removes them before routing. The
same route then answers under every stage. [Route links](/guide/route-links) covers putting the
stage back into the links that the application builds.

A handler's `CancellationToken` is not cancelled when the client disconnects. The AWS
[Overview](/aws/) covers the deadline that does cancel it.

## What the function answers

| The response's | Payload format 2.0 | Payload format 1.0 |
|---|---|---|
| Status | `statusCode`, 200 when the handler set none | `statusCode`, and for a load balancer also `statusDescription`, such as `404 Not Found` |
| Headers | `headers`. A header with several values goes as one, its values joined with commas | `multiValueHeaders`, one array for each header. A load balancer whose target group has multi-value headers off gets `headers` instead |
| Cookies | `cookies`, one `Set-Cookie` string for each cookie | `Set-Cookie` in `multiValueHeaders`, one value for each cookie |
| Body | `body`: text, or base64 with `isBase64Encoded` set to `true` when the response is marked binary | The same |

## Binary responses

`IsBinary` on the response marks the body as binary. A response compressed by Hardened's response
compression is marked binary.

The response is `IExecutionResponse`, in `Hardened.Requests.Abstract.Execution`. A handler marks it
binary through an `IExecutionContext` parameter, with `context.Response.IsBinary = true`. That
interface is in the same namespace. The body is then sent base64-encoded. The client receives the
bytes as written. The library's `[BasePath("/todos")]` puts this handler at `/todos/badge`:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public class BadgeController
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Get("/badge")]
    [Produces("image/png")]
    public byte[] Badge(IExecutionContext context)
    {
        context.Response.IsBinary = true;

        return Signature;
    }
}
```

```console
$ curl -s http://localhost:5080/todos/badge | xxd
00000000: 8950 4e47 0d0a 1a0a                      .PNG....
```

Without the `IsBinary` line, the same handler sends this:

```console
$ curl -s http://localhost:5080/todos/badge | xxd
00000000: efbf bd50 4e47 0d0a 1a0a                 ...PNG....
```

::: warning
A handler that returns `byte[]` or `Stream` does not mark its response binary. The handler still
answers 200. In `buffered` [response mode](#response-mode), the default, and in `mixed`, each byte
of the body that is not valid UTF-8 reaches the client as the three bytes `EF BF BD`, the Unicode
replacement character. An image or a PDF arrives corrupted, with no error. Set
`context.Response.IsBinary = true` in such a handler.
:::

## Failures

| Case | The function answers |
|---|---|
| The handler throws | 500 with the application's error body |
| A refusal, such as a failed validation | Its status and error body, 400 for a failed validation |
| No route matches the path | 404 with the error body |

The invocation succeeds in each case. It fails only when the adapter cannot read the event.
[Limits](#limits) has the case.

## Response mode

`HARDENED_LAMBDA_RESPONSE_MODE` sets how a response leaves the function. The value has to match the
front door in front of the function:

| `HARDENED_LAMBDA_RESPONSE_MODE` | The response leaves the function | In front of the function |
|---|---|---|
| `buffered`, the default | As one answer when the handler finishes | An HTTP API, a REST API, a load balancer, or a function URL in `BUFFERED` invoke mode |
| `stream` | As a Lambda response stream that opens at the first byte of the body | A function URL in `RESPONSE_STREAM` invoke mode |
| `mixed` | As a Lambda response stream when the body is an event stream or NDJSON, and otherwise as one answer when the handler finishes | A front door that takes a streamed or a buffered answer from each invocation |

Under `mixed`, each invocation decides at the first byte of the body. A body whose `Content-Type` is
`text/event-stream` or `application/x-ndjson` opens a Lambda response stream. Any other body goes
out as one payload. An answer with no body never opens a stream, so it goes out as one payload with
no body. The Lambda Runtime API takes either kind of answer from any invocation. Whether a function URL
in `RESPONSE_STREAM` invoke mode reads an answer sent as one payload has not been checked, so
`stream` is the value for one.

The variable is read once, at startup. Case and surrounding spaces are ignored. An unset or empty
variable means `buffered`. Any other value stops the application at startup, before the first
request:

```text
Unhandled exception. System.InvalidOperationException: HARDENED_LAMBDA_RESPONSE_MODE is 'streaming'. It must be 'buffered', 'stream' or 'mixed'. A function URL in RESPONSE_STREAM invoke mode takes 'stream'. A front door that takes a streamed or a buffered answer from each invocation takes 'mixed'. Every other deployment takes 'buffered'.
```

`ConfigureLambdaResponseMode`, in `Hardened.Aws.Lambda.Runtime.Streaming`, sets the mode in code.
It runs after the variable is read, so the function uses the mode it sets. A second part of the
template's `Application`, in `src/Todos.Host/ResponseMode.cs`, calls it:

```csharp
using DependencyModules.Runtime.Interfaces;
using Hardened.Aws.Lambda.Runtime.Streaming;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.ConfigureLambdaResponseMode(mode => mode.Mode = LambdaResponseMode.Stream);
    }
}
```

The table compares what the client receives in each mode:

| | `buffered` | `stream` | `mixed` |
|---|---|---|---|
| Status, headers and cookies are sent | When the handler finishes | With the first byte of the body | With the first byte of an event stream or NDJSON body, and otherwise when the handler finishes |
| A status or header set after a stream's first item | Is sent | Is not sent | Is not sent |
| A refusal or an exception before the first byte | Sent with its status and error body | Sent with its status and error body | Sent with its status and error body |
| A `byte[]` body not marked binary | Bytes that are not valid UTF-8 are replaced | Sent as written | Bytes that are not valid UTF-8 are replaced |
| A response with no body, such as a 204 | No body | A body of one newline | No body |

Under `stream` and `mixed`, only a function that serves routes answers with a stream. A function
that serves a trigger or `[HardenedFunction]` answers buffered.

The [AWS Lambda Test Tool](#running-it-locally) cannot run `stream`. The first request gets no
answer. After 30 seconds the process exits with
`FormatException: The input string '5050/Todos.Host' was not in a correct format.` Under `mixed`,
a route that answers an event stream or NDJSON gets no answer, and the other routes answer. The AWS
[Testing](/aws/testing) page covers running `stream` and `mixed` in a test.

### Server-sent events in buffered mode

In `buffered` mode, the events of a handler that answers `text/event-stream` all arrive together
when the handler finishes. That is a `[ServerSentEvents]` handler, or an operation whose contract
declares an event stream. [Streaming responses](/guide/streaming) covers these handlers. At startup in `buffered`
mode, an application that has any of them logs a warning that names them. For a handler at
`/todos/events` in the template, the local run prints:

```json
{"timestamp":"2026-09-28T00:25:41.303Z","level":"Warning","message":"[Hardened.Aws.Lambda.Runtime.Streaming.ServerSentEventsResponseModeStartupService] HARDENED_LAMBDA_RESPONSE_MODE is buffered and 1 handler(s) answer text/event-stream: GET /todos/events. Their events are delivered when the invocation ends, or never if it times out first. Deploy behind a function URL in RESPONSE_STREAM invoke mode with HARDENED_LAMBDA_RESPONSE_MODE=stream, or behind a front door that takes a streamed or a buffered answer from each invocation with HARDENED_LAMBDA_RESPONSE_MODE=mixed, or stop answering them as event streams: remove [ServerSentEvents] from a handler written in C#, or the event stream from the operation in its contract.","Variable":"HARDENED_LAMBDA_RESPONSE_MODE","Count":1,"Handlers":"GET /todos/events"}
```

The warning names each handler by its verb and the path a request uses, which starts with the
module's `[BasePath]`. A handler that streams NDJSON gets no warning.

## Running it locally

The template's `Program.cs` starts the AWS Lambda Test Tool with this line:

```csharp
using Hardened.Aws.Lambda.Runtime.Development;

using var emulator = await LambdaEmulator.StartIfLocal(typeof(Application), apiGateway: true);
```

[Hosts](/guide/hosts) shows the whole file, its two ports and the pinned version of the tool.

`apiGateway: true` puts the tool's API Gateway emulator in front of the function, on the port in
`PORT`, 5080 by default. The emulator sends every method on every path to the function as a payload
format 2.0 event. It does not pass on the `cookies` of the function's answer, so a local client
receives no `Set-Cookie`.

Without `apiGateway: true`, the tool starts without the emulator. The application prints only
`Started the AWS Lambda Test Tool on http://localhost:5050`. Nothing listens on 5080.

`stream` does not run locally, and `mixed` runs only the routes that do not stream, as
[Response mode](#response-mode) describes.

## Deploying

The function serves any of these front doors:

- an API Gateway HTTP API, with its Lambda integration on payload format version 2.0 or 1.0;
- an API Gateway REST API, with a Lambda proxy integration;
- a function URL;
- an Application Load Balancer, with the function as a target group's target.

Each reads only its own response format. The function answers in the format of the event it was
sent. Set `HARDENED_LAMBDA_RESPONSE_MODE` to the value that [Response mode](#response-mode) lists
for it. The AWS [Overview](/aws/) covers the rest of a deployment.

## Limits

A load balancer whose target group has multi-value headers off takes one `Set-Cookie` header. When
the response sets several cookies, only the last one reaches the client. Turning on multi-value
headers for the target group sends all of them.

For an event in payload format 2.0, the adapter splits every request header value at its commas. A
`string` parameter bound to such a header reads the parts joined by a comma with no space.
`X-Date: Tue, 01 Sep 2026 12:00:00 GMT` binds `Tue,01 Sep 2026 12:00:00 GMT`.

For the same reason, `If-Modified-Since` in a payload format 2.0 event never matches. `[ConditionalGet]` answers 200 to a request
that sends only `If-Modified-Since`. A request with `If-None-Match` gets a 304, as on other hosts.
[Conditional requests](/guide/conditional-requests) covers both headers.

## Next

| Page | Covers |
|---|---|
| [Overview](/aws/) | The packages, the entry point and deploying a Lambda function |
| [Hosts](/guide/hosts) | The Lambda host's `Program.cs` and its local ports |
| [Streaming responses](/guide/streaming) | Streaming a response, and which hosts stream |
| [Testing](/aws/testing) | Testing a web application on Lambda |
| [Invocations](/aws/invoke) | A function that a caller invokes directly |
