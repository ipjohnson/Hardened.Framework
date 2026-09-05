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
| Request body | `[FromBody]`, or inferred for a complex type with no other source |
| Container | `[FromServices]`, or inferred for a registered service type |
| Custom | An attribute implementing `ICustomBindingAttribute` |

An unattributed parameter is resolved by elimination. A name matching a path token binds from the
path, a type the container knows binds from the container, and what is left is the body.

```csharp
[Post("/body/{label}")]
public string BodyWithPath(string label, MathAddModel model) => $"{label}:{model.Values.Count}";
```

## Types

Path tokens, query values and headers arrive as strings and are converted to the declared type:

```csharp
[Get("/path-typed/{count}")]
public int TypedPathToken(int count) => count * 2;

[Get("/query-typed")]
public int TypedQuery([FromQueryString] int page) => page + 1;
```

The body is deserialized as JSON into the parameter type. A value that fails to parse as its type
answers 400 with the [validation envelope](/guide/validation#the-400-envelope).

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
