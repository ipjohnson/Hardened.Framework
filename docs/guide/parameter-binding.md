# Parameter binding

Every argument a handler takes is bound by code emitted for that handler's exact signature. The
decisions are made during the build and written out as ordinary C#, so a parameter that cannot be
bound is a build failure rather than a runtime one.

```csharp
[Get("/mixed/{id}")]
public string Mixed(
    string id,                                  // the path token
    [FromQueryString] string filter,            // ?filter=
    [FromHeader("X-Tenant")] string tenant,     // a request header
    IMathService<int> mathService) {            // the container
    return $"{id}|{filter}|{tenant}|{mathService.Add(1, 2)}";
}
```

`GET /mixed/id-9?filter=active` with `X-Tenant: acme` yields `id-9|active|acme|3`.

## The sources

| Source | How a parameter selects it |
|---|---|
| Path token | The parameter name matches a `{token}` in the route |
| Query string | `[FromQueryString]`, or `[FromQueryString("q")]` to name it |
| Header | `[FromHeader("X-Tenant")]` |
| Cookie | `[FromCookie]`, or `[FromCookie("session")]` to name it |
| Form field | `[FromForm]`, or `[FromForm("username")]` to name it |
| Request body | `[FromBody]`, or inferred for a complex type with no other source |
| Request body, unread | A `byte[]` or `Stream` parameter — see [A body that is bytes](#a-body-that-is-bytes) |
| Container | `[FromServices]`, or inferred for a registered service type |
| Custom | An attribute implementing `ICustomBindingAttribute` |

An unattributed parameter is resolved by elimination. A name matching a path token binds from the
path, a type the container knows binds from the container, and what is left is the body.

```csharp
[Post("/body/{label}")]
public string BodyWithPath(string label, MathAddModel model) => $"{label}:{model.Values.Count}";
```

### Form fields

`[FromForm]` reads a field of an `application/x-www-form-urlencoded` body:

```csharp
[Post("/sign-in")]
public IResult SignIn([FromForm] string username, [FromForm] string password) => ...;
```

It is explicit rather than inferred. A parameter the route does not declare binds from the body,
and switching that to a form field whenever the content type happened to be a form would make a
handler's binding depend on what the caller sent rather than on what the handler declared.

A handler cannot bind form fields and a body model at once — there is one body and the two readings
are different. The generator reports that combination rather than leaving one of them to come back
empty.

Fields only. `multipart/form-data`, which is what a form with a file input posts, is a different
wire format and is not read by this.

## Types

Path tokens, query values and headers arrive as strings and are converted to the declared type:

```csharp
[Get("/path-typed/{count}")]
public int TypedPathToken(int count) => count * 2;

[Get("/query-typed")]
public int TypedQuery([FromQueryString] int page) => page + 1;
```

The body is deserialized as JSON into the parameter type. A value that fails to parse as its type
answers 400 with the [validation envelope](/guide/validation#the-failure-response).

### A body that is bytes

A body parameter declared `byte[]` or `Stream` is the payload. Nothing deserializes it and the
inbound `Content-Type` does not select anything, because there is nothing to select between.

```csharp
[Put("/devices/{id}/firmware")]
public Task<Response<NoContent, NotFound>> Upload(string id, byte[] image) => ...;
```

This is the same rule the response side applies to the same two types. A handler that returns
`byte[]` or `Stream` writes its own bytes and no serializer is consulted, and one that takes them
reads its own bytes for the same reason.

`Stream` is the transport's own body, unread, which is the point of asking for one rather than a
`byte[]`: an upload larger than the process wants to hold goes to its destination a chunk at a
time. It is readable only while the request is.

A request carrying no bytes answers 400 naming the parameter, the same refusal any other missing
body gets. Declare `byte[]?` to accept one instead. A `Stream` parameter cannot be checked that
way, because emptiness is not a property of a stream that can be read without consuming it, so a
handler that cares finds out by reading.

The published operation says `application/octet-stream` with a body of
`{"type": "string", "format": "binary"}`.

Base64 inside a JSON document is still read as `byte[]` on a *member* of a model. It is only the
whole body that changes meaning.

## Naming

`[FromQueryString]` and `[FromHeader]` bind by parameter name when given no argument, and by the
supplied name otherwise. A header almost always needs the argument, because `X-Tenant` is not a
C# identifier:

```csharp
[Get("/query")]
public string ByParameterName([FromQueryString] string name) => name;          // ?name=

[Get("/query-named")]
public string ByAttributeName([FromQueryString("q")] string search) => search;  // ?q=

[Get("/header")]
public string Tenant([FromHeader("X-Tenant")] string tenant) => tenant;
```

## A lambda has no template

Everything above is a handler method, whose route template says which parameters are path tokens. A
route [registered at startup with a lambda](/guide/routing#a-lambda-instead-of-a-controller) has no
template to read, so the type decides instead: a type that can be read from a string is a path
token, matched by name when the route registers, and anything else is the request body.

The attributes, the special types and the container all behave as they do here. Only the last two
rows of [The sources](#the-sources) differ, and only for that form.

## Custom binding

An attribute implementing `ICustomBindingAttribute` takes over a parameter:

```csharp
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

public class TestFilterAttribute : Attribute, ICustomBindingAttribute {
    private readonly string _value;

    public TestFilterAttribute(string value) {
        _value = value;
    }

    public ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) {
        if (typeof(T) == typeof(string)) {
            return new ValueTask<T>((T)(object)_value);
        }

        throw new NotSupportedException("Not supported");
    }
}
```

```csharp
[Get("/test")]
public Task<string> TestValue([TestFilter("somevalue")] string testValue) =>
    Task.FromResult(testValue);
```

`BindValue<T>` is called with the parameter's declared type, which is why the example checks
`typeof(T)`. It receives the execution context, so it can read the request, the request-scoped
service provider, or anything a filter earlier in the pipeline left behind. That is how the AWS
package implements `[NewImage]` and `[OldImage]` on a
[DynamoDB stream handler](/aws/ddb-streams): both read a record the pipeline put into the request
scope.

## What it looks like generated

Turn on `EmitCompilerGeneratedFiles` and the binding for the mixed handler above is a method that
reads each source in order and calls your method. Nothing inspects `ParameterInfo` and nothing
looks a name up in a dictionary of conventions.

## Next

- [Validation](/guide/validation): constraints on the values that were bound
- [Routing](/guide/routing#path-tokens): the tokens a path declares
- [Registering services](/guide/services#injecting-into-handlers): services as method parameters
