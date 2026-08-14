# Templates

A handler returns a model. A template turns that model into HTML. Neither knows about the other
until a request asks for `text/html` — so the same handler serialises as JSON for an API client and
renders a page for a browser, without a line of code about content types.

Rendering is done by [RazorBlade](https://github.com/ltrzesniewski/RazorBlade), which compiles
`.cshtml` files into C# classes at build time. A property that does not exist on the model is a
build failure, not a blank in the page.

## Why RazorBlade

Razor normally arrives with ASP.NET Core attached. The Razor SDK emits `RazorCompiledItemAttribute`,
three MVC namespace imports and five injected MVC properties into every compiled document, so
anything built on it needs a framework reference to `Microsoft.AspNetCore.App` — including a project
that never touches MVC. That rules it out for the Lambda runtimes, which are plain
`Microsoft.NET.Sdk` projects.

RazorBlade bundles the Razor parser inside its analyzer instead. The parser runs in the compiler and
never reaches your output, so a compiled view inherits from an ordinary base class and references
nothing but it. The same views work under Kestrel, ASP.NET Core and Lambda.

## Installing

Reference both packages:

```xml
<ItemGroup>
    <PackageReference Include="RazorBlade" Version="1.0.0" />
    <PackageReference Include="Hardened.Templates.RazorBlade" Version="1.0.0-preview*" />
</ItemGroup>
```

Both, not just the Hardened one. RazorBlade ships no `buildTransitive/` folder, and MSBuild props do
not flow transitively — so referencing only `Hardened.Templates.RazorBlade` means the `.props` that
globs `**/*.cshtml` never reaches your project. Your views compile to nothing, with no error.

Then apply the module:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]
[RazorBladeTemplateLibrary]
public partial class Application { }
```

::: warning ASP.NET Core hosts
RazorBlade warns with `RB0006` when a project also uses the Razor SDK, because both generators would
process the same `.cshtml` files. Set `EnableDefaultRazorBladeItems=false` and list your views
explicitly, or keep them out of the Razor SDK's default globs.
:::

## Writing a view

```razor
@* Views/Orders.cshtml *@
@using Contoso.Orders.Models
@inherits RazorBlade.HtmlTemplate<OrderListModel>

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

It is Razor, so `@foreach`, `@if` and `@(...)` all work, and `@order.Reference` is HTML-encoded.
`@Html.Raw(value)` opts out when you mean to emit markup.

Two base classes decide what the view produces:

| Base class | Content type | Encoding |
|---|---|---|
| `RazorBlade.HtmlTemplate<T>` | `text/html; charset=utf-8` | HTML-encoded |
| `RazorBlade.PlainTextTemplate<T>` | `text/plain; charset=utf-8` | none |

The content type comes from the base class rather than the file extension, so a `.cshtml` file can
legitimately produce plain text.

## Registering views by name

A handler names a view as a string, so something has to map that name to the compiled class:

```csharp
using DependencyModules.Runtime.Attributes;
using Hardened.Templates.RazorBlade;

[SingletonService]
public class AppTemplates : IRazorBladeTemplateSource {
    public IEnumerable<RazorBladeTemplateDescriptor> Templates => [
        RazorBladeTemplate.Html<OrderListModel>("Orders", model => new Views.Orders(model)),
        RazorBladeTemplate.PlainText<Receipt>("Receipt", model => new Views.Receipt(model))
    ];
}
```

The lambda is where the untyped model the engine holds becomes the typed one the view needs. The cast
is written by the compiler, not by reflection, so this stays AOT-clean.

Registration lives in your application because RazorBlade generates view classes as `internal` by
default — `Views.Orders` is not nameable from the Hardened package. Set
`RazorBladeDefaultAccessibility` to `public` if a library needs to ship views.

Every registered source is merged, so a library can ship views alongside your own. Where two sources
use the same name, the later registration wins.

`RazorBladeTemplate.Create` takes an arbitrary content type for anything that is neither HTML nor
plain text:

```csharp
RazorBladeTemplate.Create<Report>("Export", "text/csv", model => new Views.Export(model))
```

## Naming a view from a handler

`[Template]` says which view renders the return value:

```csharp
using Hardened.Requests.Abstract.Attributes;

public class OrderController {
    [Get("/orders")]
    [Template("Orders")]
    public OrderListModel List() => _orders.Recent();
}
```

It works the same way on the implementation of a
[generated OpenAPI service interface](/guide/openapi):

```csharp
[Handler]
public class OrderServiceImpl : IOrderService {
    [Template("Orders")]
    public Task<OrderListModel> ListOrders() => _orders.RecentAsync();
}
```

That is usually where it belongs for a spec-first application. The document declares that the
operation answers `text/html`; which view produces that HTML is how your implementation fulfils it,
so changing views or engines does not edit your API description. A spec can still declare
`x-hardened-template` as a default, and the attribute overrides it.

## What actually gets rendered

Naming a view does not force HTML. `[Template]` makes a view *available*; the client's `Accept`
header decides whether it is used:

| Request | Response |
|---|---|
| `Accept: text/html` | The rendered view |
| `Accept: application/json` | The model, serialised |
| `Accept: */*`, or no header | The rendered view — the template serializer is ordered ahead of JSON |

One handler, one return value, two representations. See
[Content negotiation](/guide/content-negotiation) for how that choice is made and how to influence
it.

To render a view regardless of what the caller asked for, commit the response to a content type with
`[RawResponse]` or by setting `Response.ContentType` — a committed content type skips negotiation.

## Layouts, sections and partials

These are RazorBlade's rather than Hardened's, so its
[documentation](https://github.com/ltrzesniewski/RazorBlade) is the reference. In outline: a layout
is a view deriving from `HtmlLayout`, a view opts into one with
`@implements IUsesLayout<Views.Layout>`, and `RenderPartialAsync` composes views. Layout is a typed
relationship between two classes, not a filename convention.

## Writing another engine

`ITemplateEngine` is the seam, and RazorBlade is one implementation of it:

```csharp
public interface ITemplateEngine {
    bool CanRender(string templateName);
    string? ContentTypeFor(string templateName);
    Task RenderAsync(string templateName, object? model, IExecutionContext context);
}
```

`ContentTypeFor` is asked before rendering, because the media type a view produces is what decides
whether it is what the client wanted. It is a lookup rather than a property because it varies per
view — one engine serves both HTML and plain-text templates.

Register an implementation with `RegistrationType.Add`. Engines are resolved as a set and tested in
reverse registration order, so an application's engine is asked before one a library registered.
