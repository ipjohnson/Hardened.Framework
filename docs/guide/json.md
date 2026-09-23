# JSON serialization

Hardened reads request bodies and writes response bodies as JSON with System.Text.Json. A
`JsonSerializerContext` registered as an `IJsonTypeInfoResolver` supplies the metadata for the types
it declares to the JSON serializers of request bodies, response bodies and the items of a streamed
response.

The `hardened-web` template writes one, `TodosJsonContext`, in `src/Todos/TodosJsonContext.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Todos;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(NewTodo))]
[JsonSerializable(typeof(List<Todo>))]
public partial class TodosJsonContext : JsonSerializerContext;
```

`TodosLibrary.ConfigureServices` registers it. This excerpt of `src/Todos/TodosLibrary.cs` leaves
out the module's attributes. [Getting started](/guide/getting-started) shows the whole file.

```csharp
using System.Text.Json.Serialization.Metadata;
using DependencyModules.Runtime.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Todos;

public partial class TodosLibrary : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IJsonTypeInfoResolver>(TodosJsonContext.Default);
    }
}
```

```http
GET /todos

HTTP/1.1 200 OK
Content-Type: application/json

[{"id":1,"title":"Read the generated code","done":true},{"id":2,"title":"Add an endpoint","done":false}]
```

## Registering a context

`IJsonTypeInfoResolver` is in `System.Text.Json.Serialization.Metadata`. A module registers a
context as one in its `ConfigureServices`. [Modules](/guide/modules) covers
`IServiceCollectionConfiguration`.

Hardened's JSON serializers take the resolvers from the container. They do not read a context that
is not registered. They ask the registered resolvers in registration order. The first resolver that
describes a type supplies its metadata.

On a JIT host, the serializers fall back to reflection for a type that no registered resolver
describes. A Native AOT binary has no reflection fallback. [Native AOT](#native-aot) covers it.

The serializers look up a response value by its runtime type. The template's context declares
`List<Todo>` because `TodoController.All` returns the `List<Todo>` that the store builds, typed as
`IReadOnlyList<Todo>`.

With no options set, both directions use `JsonSerializerDefaults.Web`: camelCase names, names read
in any case and numbers read from JSON strings. The serializers apply their own options to a
context's metadata. They do not apply a `PropertyNamingPolicy` set in the context's
`[JsonSourceGenerationOptions]`.

## Enum values

The build writes and registers a JSON converter for each enum that a handler's parameters, request
body or response reach. The context needs no setting for enums. An enum's values are camelCase
unless `[JsonEnumNaming]` sets another naming.

The attribute and `EnumNaming` are in `Hardened.Requests.Abstract.Attributes`, in the package
`Hardened.Requests.Abstract`. The template's projects already reference that package. On the
assembly, the attribute sets the naming for the assembly's enums. On an enum, it sets that enum's
naming and wins over the assembly's. The attribute is valid only on an assembly or an enum. On a
class, the build fails with `CS0592`.

The table shows how each naming writes two members, `InProgress` and `HTTPProxy`.

| Value | `InProgress` | `HTTPProxy` |
|---|---|---|
| `MemberName` | `InProgress` | `HTTPProxy` |
| `CamelCase` (the default) | `inProgress` | `httpProxy` |
| `KebabCaseLower` | `in-progress` | `http-proxy` |
| `SnakeCaseLower` | `in_progress` | `http_proxy` |
| `SnakeCaseUpper` | `IN_PROGRESS` | `HTTP_PROXY` |

In `src/Todos/Reminders.cs`, `Priority` reaches a query parameter, a request body and a response:

```csharp
using Hardened.Web.Runtime.Attributes;

namespace Todos;

public enum Priority
{
    Low,
    InProgress,
}

public record Reminder(string Text, Priority Priority, DateOnly DueOn);

public class ReminderController
{
    [Get("/reminders")]
    public Reminder Find([FromQueryString] Priority priority) =>
        new Reminder("Call the plumber", priority, new DateOnly(2026, 10, 1));

    [Post("/reminders")]
    public Reminder Create(Reminder reminder) => reminder;
}
```

`src/Todos/AssemblyInfo.cs` sets `KebabCaseLower` for the assembly:

```csharp
using Hardened.Requests.Abstract.Attributes;

[assembly: JsonEnumNaming(EnumNaming.KebabCaseLower)]
```

The example also declares `Reminder` in the context with `[JsonSerializable(typeof(Reminder))]`.

```http
GET /todos/reminders?priority=in-progress

HTTP/1.1 200 OK
Content-Type: application/json

{"text":"Call the plumber","priority":"in-progress","dueOn":"2026-10-01"}
```

In the same application, `LegacyCode` is written with its member names:

```csharp
using Hardened.Requests.Abstract.Attributes;

namespace Todos;

[JsonEnumNaming(EnumNaming.MemberName)]
public enum LegacyCode
{
    AB12,
    CD34,
}
```

The OpenAPI document lists the same values in the enum's schema. A path, query or header parameter
binds the same values. [Parameter binding](/guide/parameter-binding) covers it. An enum used as a
dictionary key is written with the same values.

A body value that the enum does not declare answers 400 with the code `invalid`:

```http
POST /todos/reminders
Content-Type: application/json

{"text":"Call the plumber","priority":"InProgress","dueOn":"2026-10-01"}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"reminder.priority","code":"invalid","message":"\u0027InProgress\u0027 is not a value Priority declares."}]}
```

When two members share a value, the build's converter writes the first one declared. The OpenAPI
document lists that member alone.

An enum declared in another project gets a converter only when that project declares
`[assembly: JsonEnumNaming]`. That project's naming applies.

The build's converter replaces a `[JsonConverter]` on the enum type and the context's
`UseStringEnumConverter`. A `[JsonConverter]` on a property applies to that property. The OpenAPI
document still lists the `[JsonEnumNaming]` values for that property.

## Required members and absent values

The members that a request body must carry depend on whether the type is read by reflection or from
a registered context:

| Member | Type read by reflection | Type declared in a registered context |
|---|---|---|
| Constructor parameter of a non-nullable reference type with no default, such as `string Title` | Required | Not required |
| `required` member | Required | Required |
| `[JsonRequired]` member | Required | Required |
| Constructor parameter of a nullable reference type, such as `string? Note` | Not required | Not required |
| Constructor parameter of a value type, such as `int Count` | Not required | Not required |
| Constructor parameter with a default, such as `string Label = "none"` | Not required | Not required |
| Member with `[ResponseOnly]` (in `Hardened.Requests.Abstract.Attributes`) | Not required | Not required |
| Settable property with an initializer | Not required | Not required |

A Native AOT binary reads every type from a context. Only `required` and `[JsonRequired]` members
are required there.

::: warning
A type declared in a registered context does not require its non-nullable reference members. The
template declares its models in `TodosJsonContext`. `POST /todos` with `{}` answers 201 and stores a
todo whose title is `null`. The OpenAPI document still lists `title` as required.
:::

```http
POST /todos
Content-Type: application/json

{}

HTTP/1.1 201 Created
Content-Type: application/json
Location: /todos/3

{"id":3,"title":null,"done":false}
```

`[JsonRequired]` is in `System.Text.Json.Serialization`. On a positional record, it takes the
`property:` target. This excerpt of `src/Todos/TodoStore.cs` adds `JsonRequired` to `NewTodo`, with
its `using` line. The rest of the file is unchanged.

```csharp
using System.Text.Json.Serialization;
using ValidationModules.Constraints;

namespace Todos;

public record NewTodo([property: StringLength(1, 64), JsonRequired] string Title);
```

A body that leaves out a required member answers 400 with the code `required`. The `field` is the
body parameter's name and the member's JSON name, here `request.title`. The response lists every
missing member.

```http
POST /todos
Content-Type: application/json

{}

HTTP/1.1 400 Bad Request
Content-Type: application/json

{"type":"ValidationError","message":"One or more validation errors occurred.","errors":[{"field":"request.title","code":"required","message":"title is required."}]}
```

A member that is not required and is not sent reads as its default, such as `null` for a reference
type, `0` for an `int`, the first member of an enum or `0001-01-01` for a `DateOnly`. `required` or
`[JsonRequired]` makes the absence of a value-type member a refusal. `HRDV003` on
[Validation](/guide/validation) points to this section.

A sent `null` is not an absence. `{"title":null}` passes the deserializer.
[Validation](/guide/validation) covers `[Required]`, which refuses it.

A model generated from a contract marks its required members itself.
[Contract-first models](#contract-first-models) covers it.

## Converters

The table lists where a converter can be declared and the bodies it applies to.

| Declared | Applied to |
|---|---|
| `[JsonConverter]` on the type | Request and response bodies, in a JIT host and in a Native AOT binary |
| `[JsonConverter]` on a property | That property, in request and response bodies |
| `Converters` in the options of `JsonSerializerConfiguration` | The direction whose options list it |
| `Converters` in the context's `[JsonSourceGenerationOptions]` | Nothing on a JIT host. Under `[AotSerializerModule]`, request bodies only |

[Replacing the serializer options](#replacing-the-serializer-options) covers
`JsonSerializerConfiguration`. [Native AOT](#native-aot) covers `[AotSerializerModule]`.

In `src/Todos/Prices.cs`, `[JsonConverter]` on `Money` names `MoneyConverter`:

```csharp
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Web.Runtime.Attributes;

namespace Todos;

[JsonConverter(typeof(MoneyConverter))]
public record Money(decimal Amount, string Currency);

public class MoneyConverter : JsonConverter<Money>
{
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var parts = reader.GetString()!.Split(' ');

        return new Money(decimal.Parse(parts[0], CultureInfo.InvariantCulture), parts[1]);
    }

    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.Amount.ToString("0.00", CultureInfo.InvariantCulture) + " " + value.Currency);
    }
}

public record Price(string Item, Money Cost);

public class PriceController
{
    [Get("/prices/{item}")]
    public Price Find(string item) => new Price(item, new Money(12.5m, "EUR"));

    [Post("/prices")]
    public Price Create(Price price) => price;
}
```

The example also declares `Price` in the context with `[JsonSerializable(typeof(Price))]`.

```http
GET /todos/prices/tea

HTTP/1.1 200 OK
Content-Type: application/json

{"item":"tea","cost":"12.50 EUR"}
```

`POST /todos/prices` with `{"item":"tea","cost":"3.20 GBP"}` returns the body it was sent.

The OpenAPI document describes the type's members, not what its converter writes. It publishes
`Money` as an object with `amount` and `currency`.

## Replacing the serializer options

`JsonSerializerConfiguration`, in `Hardened.Requests.Runtime.Configuration`, has `SerializeOptions`
for response bodies and `DeSerializerOptions` for request bodies. Both are null by default, which
means `new JsonSerializerOptions(JsonSerializerDefaults.Web)`. Options set here replace that default
whole. The serializers add the registered resolvers to the options given, ahead of reflection.

An `AppConfig` amendment sets them. [Configuration](/guide/configuration) covers `AppConfig`. In
the host project, `src/Todos.Host/ApplicationConfiguration.cs` is a second file of the
`partial class Application`:

```csharp
using System.Text.Json;
using DependencyModules.Runtime.Interfaces;
using Hardened.Requests.Runtime.Configuration;
using Hardened.Shared.Runtime.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Todos.Host;

public partial class Application : IServiceCollectionConfiguration
{
    public void ConfigureServices(IServiceCollection services)
    {
        var config = new AppConfig();

        config.Amend(
            (JsonSerializerConfiguration json) =>
            {
                json.SerializeOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                };

                json.DeSerializerOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                };
            }
        );

        services.AddSingleton<IConfigurationPackage>(config);
    }
}
```

With that file and `Reminders.cs` from [Enum values](#enum-values), the reminder route answers:

```http
GET /todos/reminders?priority=in-progress

HTTP/1.1 200 OK
Content-Type: application/json

{"text":"Call the plumber","priority":"in-progress","due_on":"2026-10-01"}
```

An enum with a build converter keeps its values whatever naming or converters the options set.

Set both options. With only `SerializeOptions` set to snake case, the application writes
`word_count` and reads `wordCount`. A body that the application wrote loses its `word_count`
value when it is posted back.

The OpenAPI document keeps the camelCase names. A client generated from it sends `dueOn`, which the
options above leave unread.

Under `[AotSerializerModule]`, request bodies take only `PropertyNameCaseInsensitive`,
`PropertyNamingPolicy`, `NumberHandling` and `Converters` from `DeSerializerOptions`.

## Contract-first models

In a project generated from an OpenAPI description or a Smithy model, the build generates the models
and a resolver for them. The build also registers the resolver. The project registers no context of
its own.

The values of a generated enum are the contract's. Neither `[JsonEnumNaming]` nor a
`JsonStringEnumConverter` in the serializer options changes them.

A generated model carries `[JsonRequired]` on each member that the contract requires. A body that
leaves one out answers 400 with the code `required` on every host.

[Generating from OpenAPI](/guide/openapi) and [Generating from Smithy](/guide/smithy) cover the
generated models.

## Native AOT

An application published with Native AOT sets `<PublishAot>true</PublishAot>` in the host project.
It also puts `[AotSerializerModule]` on the application module. The attribute is in
`Hardened.Requests.Runtime`, which the host project already references. It registers JSON
serializers that read only from the registered resolvers.

The host project's first `PropertyGroup`, in `src/Todos.Host/Todos.Host.csproj`, sets the property:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <PublishAot>true</PublishAot>
</PropertyGroup>
```

The application module, in `src/Todos.Host/Application.cs`, carries the attribute:

```csharp
using Hardened.Requests.Runtime;
using Hardened.Shared.Runtime.Attributes;
using Hardened.Web.Kestrel.Runtime;
using Hardened.Web.Runtime.OpenApi;

namespace Todos.Host;

[HardenedModule]
[KestrelRuntime]
[HardenedOpenApiUi(Title = "Todos", Environments = "development")]
[TodosLibrary]
[AotSerializerModule]
public partial class Application;
```

Without the attribute, the publish succeeds with no warning. The binary then answers every JSON
request with 500 and an empty body. The log names the cause:
`Could not locate response serializer for accept: */*`.

The context must declare every type that the application reads or writes as JSON. That includes the
runtime type of each response value and the framework's bodies that the application sends:

| Body the application sends | Type the context declares |
|---|---|
| A built-in response that the application returns | Its type, such as `NotFound` or `Conflict` |
| A 400 from binding or validation | `RequestValidationError` |
| A 500 | `ErrorModel` |

[Declared responses](/guide/responses) lists the built-in response types. With the framework's
bodies, `src/Todos/TodosJsonContext.cs` becomes:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using Hardened.Requests.Abstract.Errors;
using Hardened.Requests.Runtime.Validation;
using Hardened.Web.Runtime.Responses;

namespace Todos;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(NewTodo))]
[JsonSerializable(typeof(List<Todo>))]
[JsonSerializable(typeof(NotFound))]
[JsonSerializable(typeof(Conflict))]
[JsonSerializable(typeof(RequestValidationError))]
[JsonSerializable(typeof(ErrorModel))]
public partial class TodosJsonContext : JsonSerializerContext;
```

The native binary answers `GET /todos/99` with the `NotFound` body:

```http
GET /todos/99

HTTP/1.1 404 Not Found
Content-Type: application/json

{"resource":"todo","detail":"No todo has id 99.","type":"urn:hardened:problem:not-found","title":"Not Found","status":404}
```

Without the framework's bodies in the context, the template's 404, 409 and 400 each answer 500 with
an empty body.

A response value whose type no resolver describes answers 500 with an empty body. A request body
whose type no resolver describes answers 500 with the `ServerError` body. Both log a
`NotSupportedException` that names the type.

The build's enum converters and views work in a Native AOT binary. [Views](/guide/views) covers
views.

The table gives the result of a Native AOT publish on each host.

| Host | Native AOT |
|---|---|
| Kestrel | Publishes and serves |
| ASP.NET Core | Publishes and serves |
| AWS Lambda | Publishes and serves |
| Google Cloud Run | Publishes and serves |
| Google Cloud Functions | Cannot be trimmed or published with Native AOT: the Functions Framework loads the function's entry type by reflection |
| Azure Functions | The Native AOT publish builds, and the worker does not start as a native binary ([Azure/azure-functions-dotnet-worker#1056](https://github.com/Azure/azure-functions-dotnet-worker/issues/1056)). A trimmed, self-contained publish runs |

[Hosts](/guide/hosts) shows each host's `Program.cs`.

## Limits

The build writes no converter for a `[Flags]` enum, or for an enum from another project that
declares no `[assembly: JsonEnumNaming]`. Their values are written as numbers. The OpenAPI document
lists their member names as strings.

## Next

| Page | Covers |
|---|---|
| [Content negotiation](/guide/content-negotiation) | Which serializer answers a request, and error bodies |
| [MessagePack](/guide/message-pack) | MessagePack beside JSON |
| [Streaming responses](/guide/streaming) | A stream of JSON items |
| [Validation](/guide/validation) | Constraints on members, and `[Required]` |
| [The OpenAPI document](/guide/openapi-document) | The document that the enum values and `required` lists appear in |
| [Hosts](/guide/hosts) | Each host's `Program.cs` |
