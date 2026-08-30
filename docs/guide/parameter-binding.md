# Parameter binding

Every argument a handler takes is bound by code emitted for that handler's exact signature. The
binding decisions are made during the build and written out as ordinary C#, so a parameter that
cannot be bound is a build failure.

## The sources

```csharp
[BasePath("/binding")]
public class BindingController {

    [Get("/path/{id}")]
    public string FromPath(string id) => id;

    [Get("/query")]
    public string FromQuery([FromQueryString] string name) => name;

    [Get("/header")]
    public string FromHeader([FromHeader("X-Tenant")] string tenant) => tenant;

    [Post("/body/{label}")]
    public string BodyWithPath(string label, MathAddModel model) => $"{label}:{model.Values.Count}";
}
```

| Source | How a parameter selects it |
|---|---|
| Path token | The parameter name matches a `{token}` in the route |
| Query string | `[FromQueryString]`, optionally `[FromQueryString("q")]` |
| Header | `[FromHeader("X-Tenant")]` |
| Request body | `[FromBody]`, or inferred for a complex type with no other source |
| Container | `[FromServices]`, or inferred for a registered service type |
| Custom | An attribute implementing `ICustomBindingAttribute` |

Unattributed parameters are resolved by elimination: a name matching a path token binds from the
path, a type the container knows binds from the container, and what is left over is the body.

## Types

Path tokens, query values and headers arrive as strings and are converted to the declared type:

```csharp
[Get("/path-typed/{count}")]
public int TypedPathToken(int count) => count * 2;

[Get("/query-typed")]
public int TypedQuery([FromQueryString] int page) => page + 1;
```

The body is deserialised as JSON into the parameter type.

## Naming

`[FromQueryString]` and `[FromHeader]` bind by parameter name when given no argument, and by the
supplied name otherwise. Headers almost always need the argument, because `X-Tenant` is not a legal
C# identifier:

```csharp
[Get("/query")]
public string ByParameterName([FromQueryString] string name) => name;      // ?name=…

[Get("/query-named")]
public string ByAttributeName([FromQueryString("q")] string search) => search; // ?q=…

[Get("/header")]
public string Tenant([FromHeader("X-Tenant")] string tenant) => tenant;
```

## Mixing sources

Sources compose in a single signature, in any order:

```csharp
[Get("/mixed/{id}")]
public string Mixed(
    string id,
    [FromQueryString] string filter,
    [FromHeader("X-Tenant")] string tenant,
    IMathService<int> mathService) {
    return $"{id}|{filter}|{tenant}|{mathService.Add(1, 2)}";
}
```

`GET /binding/mixed/id-9?filter=active` with `X-Tenant: acme` yields `id-9|active|acme|3`.

## Custom binding

An attribute implementing `ICustomBindingAttribute` takes over a parameter entirely:

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

`BindValue` receives the execution context, so it can reach the request, the request-scoped service
provider, or anything a filter earlier in the pipeline left behind. This is how the AWS package
implements `[NewImage]` and `[OldImage]` on a
[DynamoDB stream handler](/aws/ddb-streams) — both pull from a record the pipeline put into the
request scope.

::: tip The generic parameter is the declared type
`BindValue<T>` is called with the parameter's type, which is why the example checks `typeof(T)` and
throws otherwise.
:::

## What it looks like generated

Turn on `EmitCompilerGeneratedFiles` and the binding for the mixed handler above is a method that
reads each source in order and calls your method. Nothing inspects `ParameterInfo` and nothing looks
a name up in a dictionary of conventions.
