# Invocations

`[HardenedFunction]` marks a handler that a caller invokes directly, through the Lambda Invoke API.
The payload binds to the handler's parameter, and the handler's return value is the invocation's
response.

```csharp
using Hardened.Requests.Abstract.Attributes;

namespace Orders;

public class OrderHandler(OrderLog log)
{
    [HardenedFunction]
    public OrderAccepted Process(Order order)
    {
        log.Record(order);

        return new OrderAccepted(order.Id, log.Orders.Count);
    }
}

public record OrderAccepted(string Id, int Received);
```

`dotnet new hardened-function -n Orders` writes this function and its tests. `Order` and `OrderLog`
are classes that the template writes in `src/Orders`. The template defaults to `--trigger invoke` and
`--host aws`. Run locally with `dotnet run --project src/Orders`, the
function `Orders` answers this invocation:

```http
POST /2015-03-31/functions/Orders/invocations
Content-Type: application/json

{"id":"A-1","quantity":2}

HTTP/1.1 200 OK
Content-Type: application/json

{"id":"A-1","received":1}
```

An SDK caller, the AWS CLI and Step Functions invoke a function through the Lambda Invoke API.

## Packages

The function's project references two packages:

```xml
<PackageReference Include="Hardened.Aws.Lambda.Runtime" Version="0.0.0-HARDENED-VERSION" />
<PackageReference Include="Hardened.Aws.Lambda.Invoke" Version="0.0.0-HARDENED-VERSION" />
```

`[HardenedFunction]` is in `Hardened.Requests.Abstract.Attributes`. The AWS [Overview](/aws/) lists
the other packages a Lambda project references.

`Hardened.Aws.Lambda.Invoke` sets the build property `HardenedInvokeModule` to
`Hardened.Aws.Lambda.Invoke.InvokeModule`. The build registers that module for a project with a
`[HardenedFunction]` handler. The application class does not name the module. The module has no
settings. The `[InvokeModule]` in `Hardened.Gcp.CloudRun.Invoke` has a `Prefix`. The Google Cloud
[Invocations](/gcp/invoke) page covers it.

## Function names

An invocation is routed by the name of the Lambda function it was sent to. The function's process
reads that name from `AWS_LAMBDA_FUNCTION_NAME`, which Lambda sets.

A handler with no name answers every invocation, whatever the function is called. A handler with a
name answers only a function of that name. `[HardenedFunction("process-order")]` answers the
function `process-order`. In a project that also has named handlers, a handler with no name answers
every function name that no named handler has. A name equal to the method's name counts as no name.

| The handler | Answers a function named |
|---|---|
| `[HardenedFunction]` | Any name no named handler has |
| `[HardenedFunction("process-order")]` | `process-order` |
| `[HardenedFunction("Process")]` on a method named `Process` | Any name no named handler has |

A function whose handler has a name is deployed under that name. One project can hold several named
handlers. The same package is then deployed as several functions, one for each name. The AWS
[Overview](/aws/) covers the rest of a deployment.

With two handlers that have no name, one of them answers every invocation.
[Testing functions](/guide/testing-functions) covers that case.

An invocation that no handler answers fails with `InvalidOperationException`. When no handler
answers the function `orders-fn`, the message is:

```text
No handler is registered for INVOKE /orders-fn. An event source is wired to this function that no trigger attribute declared.
```

## The payload and the response

The payload is the request's body, as the caller sent it. `InvokeAdapter` does not parse it. The
payload binds as JSON to the handler's body parameter. [Parameter binding](/guide/parameter-binding)
covers which parameter that is. [JSON serialization](/guide/json) covers property names and
serializer options.

The return value is the whole response:

| The handler returns | The response |
|---|---|
| `T` | The value, written as JSON |
| `Task<T>` | The same as `T` |
| `void` | An empty payload |

The custom values of the caller's client context become request headers, under the names the caller
gave them. A handler reads them from `context.Request.Headers` through an `IExecutionContext`
parameter. That interface is in `Hardened.Requests.Abstract.Execution`.

An invocation has no query string, no cookies and no `Content-Type`.

The response is never streamed. `HARDENED_LAMBDA_RESPONSE_MODE=stream` leaves it buffered.
[Web applications](/aws/lambda-web) covers the setting.

## Failures

A handler that throws fails the invocation. The caller receives a function error, whose body
describes the exception:

| Field | Contents |
|---|---|
| `errorType` | The exception's type |
| `errorMessage` | The exception's message |
| `stackTrace` | The exception's stack |

A refusal before the handler runs also fails the invocation. For a failed validation, `errorMessage`
is `One or more validation errors occurred.` and `errorType` is `ValidationException`.
[Validation](/guide/validation) covers the constraints. A payload that is not JSON the parameter can
bind fails the invocation with `JsonException`.

Hardened does not repeat a failed invocation. `[Retry]` repeats a `[HardenedFunction]` handler only
with `AllowNonIdempotent = true`. [The execution pipeline](/guide/execution-pipeline) covers
`[Retry]`.

## Running locally

`dotnet run --project src/Orders` starts the AWS Lambda Test Tool without an API Gateway emulator.
The application prints only `Started the AWS Lambda Test Tool on http://localhost:5050`. Nothing
listens on port 5080.

The tool answers the Lambda Invoke API on port 5050. In the tool's API, the function's name is the
project's assembly name, `Orders`. The tool's page at `http://localhost:5050` invokes the function
with a payload typed into it. [Hosts](/guide/hosts) covers the tool.

The AWS CLI reaches the tool with `--endpoint-url`. The CLI needs credentials and a region. Any
values work against the tool. In the example below, the CLI takes them from `AWS_REGION`,
`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`:

```console
$ aws lambda invoke --endpoint-url http://localhost:5050 --function-name Orders --cli-binary-format raw-in-base64-out --payload '{"id":"A-1","quantity":2}' response.json
{
    "StatusCode": 200
}
$ cat response.json
{"id":"A-1","received":1}
```

The tool does not pass a client context to the function.

Locally, the name that the function's process reads is empty, so only a handler with no name
answers. Without the variable, `[HardenedFunction("process-order")]` fails with
`No handler is registered for INVOKE /. An event source is wired to this function that no trigger attribute declared.`
A named handler answers when `AWS_LAMBDA_FUNCTION_NAME` is set to its name:

```bash
AWS_LAMBDA_FUNCTION_NAME=process-order dotnet run --project src/Orders
```

## Testing

`Application.Invocations` is the façade a test calls. [Testing functions](/guide/testing-functions)
covers the façade and the return types it cannot carry.

Under `[LambdaTesting]`, a call goes through `LambdaInvocationHandler` and the adapter, with the
handler's own name as the function name. A test reaches a named handler whatever name the function
is deployed under. The AWS [Testing](/aws/testing) page covers `[LambdaTesting]`.

## Events from other AWS services

The adapter takes every payload as the caller's.

::: warning
An event from SQS or API Gateway sent to an invocation function binds as the payload. In a function
that has a `[HardenedFunction]` handler beside a trigger handler, the `[HardenedFunction]` handler
takes the trigger's events. The trigger's handler never runs. Every such invocation succeeds. Give
`[HardenedFunction]` handlers a function of their own, and send it only direct invocations.
:::

## Next

- [Overview](/aws/): the packages, the entry point and deploying a Lambda function
- [Triggers](/guide/triggers): the other sources a handler can serve
- [Testing functions](/guide/testing-functions): calling an invocation from a test through
  `Application.Invocations`
- [Queues](/aws/queue): a function that SQS delivers messages to
- [Web applications](/aws/lambda-web): a function that serves HTTP routes
