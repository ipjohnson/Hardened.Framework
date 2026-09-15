# Views

A handler returns a model and a view turns it into HTML. `[Output<T>]` names the view:

```csharp
using Hardened.Requests.Abstract.Attributes;

public class OrderController {
    [Get("/orders")]
    [Output<Views.Orders>]
    public OrderListModel List() => _orders.Recent();
}
```

```razor
@* Views/Orders.cshtml *@
@using Contoso.Orders.Models
@inherits Contoso.Orders.ApplicationRazorTemplates<OrderListModel>

<table>
    <tr><th>Reference</th><th>Total</th><th>Placed</th></tr>
    @foreach (var order in Model.Orders)
    {
        <tr>
            <td>@order.Reference</td>
            <td>@order.Total.ToString("###.00")</td>
            <td>@order.PlacedAt</td>
        </tr>
    }
</table>
```

```
GET /orders
Accept: text/html
HTTP/1.1 200 OK
Content-Type: text/html; charset=utf-8
```

Rendering is [RazorBlade](https://github.com/ltrzesniewski/RazorBlade), which compiles `.cshtml`
files into C# classes at build time. A property that does not exist on the model is a build
failure, not a blank in the page. A compiled view has no ASP.NET Core dependency, so the same
views work under Kestrel, ASP.NET Core and Lambda.

::: tip Looking for `dotnet new`?
This page is about rendering HTML. The project templates are under
[Project templates](/guide/project-templates).
:::

## Installing

Reference both packages:

```xml
<ItemGroup>
    <PackageReference Include="RazorBlade" Version="1.0.0" />
    <PackageReference Include="Hardened.Templates.RazorBlade" Version="0.0.0-HARDENED-VERSION" />
</ItemGroup>
```

Both, not just the Hardened one. RazorBlade ships no `buildTransitive/` folder and MSBuild props
do not flow transitively, so referencing only `Hardened.Templates.RazorBlade` means the `.props`
that globs `**/*.cshtml` never reaches your project. Your views compile to nothing, with no
error.

Then turn it on for a module:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[Enable<RazorTemplates>]
public partial class Application { }
```

That generates `ApplicationRazorTemplates<TModel>`, named from the entry point plus the marker,
which is what your views inherit. `[Enable<T>]` is the framework's one name for every optional
generated feature; type `[Enable<` and let completion list what the project has referenced.

::: warning ASP.NET Core hosts
RazorBlade warns with `RB0006` when a project also uses the Razor SDK, because both generators
would process the same `.cshtml` files. Set `EnableDefaultRazorBladeItems=false` and list your
views explicitly, or keep them out of the Razor SDK's default globs.
:::

## Writing a view

It is Razor, so `@foreach`, `@if` and `@(...)` all work, and `@order.Reference` is HTML-encoded.
`@Html.Raw(value)` opts out when you mean to emit markup.

The marker decides what the view produces:

| Marker | Base | Content type | Encoding |
|---|---|---|---|
| `RazorTemplates` | `HardenedHtmlTemplate<T>` | `text/html; charset=utf-8` | HTML-encoded |

The content type comes from the marker rather than the file extension. Two markers on one module
produce two bases, `ApplicationRazorTemplates<T>` and `ApplicationFluidTemplates<T>`.

## Naming a view from a handler

`[Output<T>]` takes a type, not a name. Because the attribute is applied in your own assembly,
RazorBlade's `internal` generated view classes are nameable there. There is nothing to register.
The generated handler puts a factory on the response and the view renders itself.

It works the same way on the implementation of a
[generated service interface](/guide/openapi):

```csharp
[Handler]
public class OrderServiceImpl : IOrderService {
    [Output<Views.Orders>]
    public Task<OrderListModel> ListOrders() => _orders.RecentAsync();
}
```

The document declares that the operation answers `text/html`. Which view produces that HTML is
how your implementation fulfils it, so changing views or engines does not edit your API
description. A document that promises `text/html` for a model and an implementation that names
no view is a build error.

## What the compiler checks, and where

On the attribute: `OutputAttribute<T>` constrains `T` to `IHardenedResponseOutput, new()`, and
it binds in the final compilation where RazorBlade's output exists. A type that is not an output,
or has no parameterless constructor, is an error on the attribute, naming the type.

In generated code: that the view's model matches the handler's return type cannot be expressed on
the attribute, because the view is another generator's output. So the generator emits an
assignment the compiler has to bind:

```csharp
private static readonly IHardenedResponseOutput<OrderListModel> _outputCheck_List = new Views.Orders();
```

A mismatch reads `cannot convert Views.Orders to IHardenedResponseOutput<OrderListModel>`, naming
both types.

## Choosing a view per request

The response carries a factory, assigned before the handler runs, so a handler or a filter can
replace it: a different view for mobile than for desktop, an A/B test, an error view.

```csharp
context.Response.OutputFactory = static _ => new Views.OrdersMobile();
```

One construction shape only: the view is constructed with no arguments and the model attached
afterwards.

## Links in a view

A view built on a generated base has a `Links` property:

```razor
<a href="@Links.Orders.Order(order.Id)">@order.Reference</a>
```

RazorBlade copies `@` expressions verbatim and emits `#line` directives with exact spans, so
renaming the route or its handler breaks the template at build time, reported at its own line
and column. See [Routes by name](/guide/routing#routes-by-name).

## What gets rendered

Declaring an output takes the response out of negotiation. The view renders, whatever the client
asked for:

| Request | Response |
|---|---|
| `Accept: text/html` | The rendered view |
| `Accept: */*`, or no header | The rendered view |
| `Accept: application/json` | The rendered view |

A view usually renders a subset of what its model holds, so falling back to JSON would put the
rest of the model on the wire from a route whose author wrote nothing but a view. Adding
`[Output<T>]` to a handler can never widen what it discloses.

Refusing an `Accept` the view does not produce is the other way to avoid that, and it costs more
than it buys. A client generated from the document pins `Accept: application/json` on every
method, so a view route that refuses that header refuses every call the generated client makes.

To serve both representations from one handler, do not declare an output: return the model and
let [content negotiation](/guide/content-negotiation) choose a serializer.

## What the document says

A code-first operation with an output publishes the media type the output writes, and a body of
`type: string`:

```json
"200": { "content": { "text/html; charset=utf-8": { "schema": { "type": "string" } } } }
```

The media type comes from the `[TemplateContentType]` on the marker the module enabled, which is
the same value the generated base writes onto the response. An application that enables no
template engine, or more than one, publishes `text/html`; a handler that wants something else
declares `[Produces]`, which is read ahead of the marker.

The schema is `string` rather than the model because the model is not what goes on the wire. A
`$ref` to it would have a generated client parse markup as the model.

Refusals keep their own bodies. A handler that threw has no model to render, so the output is
never reached and the error goes out under the [error-body
policy](/guide/content-negotiation) like any other.

## Layouts and sections

These are RazorBlade's rather than Hardened's, so its
[documentation](https://github.com/ltrzesniewski/RazorBlade) is the reference. What follows is the
spelling that compiles against the version this page tells you to reference.

A layout is a view deriving from `HtmlLayout`:

```razor
@inherits global::RazorBlade.HtmlLayout
<html><body><main>@RenderBody()</main></body></html>
```

A view names its layout by overriding `CreateLayout` in a `@functions` block:

```razor
@inherits global::Hardened.Templates.RazorBlade.HardenedHtmlTemplate<Models.FortunePage>
<p>@Model.Fortunes.Count</p>
@functions {
    protected override global::RazorBlade.HtmlLayout? CreateLayout() => new Views.Shell();
}
```

The return type is `HtmlLayout`, not the `IRazorLayout` the base declares: that interface is
internal, so an override written against it is `CS0122`.

Sections are `DefineSection` and `RenderSection`. There is no partial: a view composes by rendering
another view's output itself.

Three spellings that do not work, all of which this page named until 0.37:

| Written | What happens |
|---|---|
| `@implements IUsesLayout<Views.Shell>` | `CS0246`. No such type, in the assembly or the analyzer |
| `@{ Layout = new Views.Shell(); }` | `CS0200`. `HtmlTemplate.Layout` is read only |
| `RenderPartialAsync(...)` | `CS0103`. No such method on any RazorBlade base |

## Writing another engine

There is no engine interface to implement. An output writes the response itself, so what another
engine ships is a marker and a base:

```csharp
[TemplateBase(typeof(FluidTemplate<>))]
[TemplateContentType("text/html; charset=utf-8")]
public sealed class HardenedFluidTemplate { }
```

The generator resolves whichever marker `[Enable<T>]` names, reads those two attributes and emits
a base deriving from what the first points at. The base implements
`IHardenedResponseOutput<TModel>`, which is one method:

```csharp
Task WriteOutput(IExecutionContext context);
```

The model is on `context.Response.ResponseValue`; the base reads it, casts once, and renders. A
template base also exposes `protected IExecutionContext Context`, which is what the generated
`Links` property resolves from. A marker may also be a DependencyModules module, so a package
shipping services alongside a generated type is one attribute rather than two.

## Next

- [Content negotiation](/guide/content-negotiation): serving JSON and HTML from one handler
- [Routing](/guide/routing#routes-by-name): the generated links a view uses
- [Sending requests](/guide/testing-web#the-response): reading a rendered page in a test
