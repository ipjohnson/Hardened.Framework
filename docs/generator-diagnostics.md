# Generator diagnostics

Every diagnostic the Hardened source generators raise, what causes it, and how to satisfy it.

Warnings become errors under `ContinuousIntegrationBuild`, so anything left unaddressed locally
fails CI. That is deliberate — the alternative is a warning nobody reads. Each entry below names the
`NoWarn` for the cases where the warning is describing something you meant to do.

## Handler binding

### HOAG030 — described service has no handler

A description declares a service and no class carrying `[Handler]` implements it. The routes exist in
the routing table and fail when a request reaches them.

```
'IPetService' is declared by the description but no class carrying [Handler] implements it,
so its 4 route(s) exist and fail at request time.
```

Implement the interface, or silence it if this project is meant to ship the generated interfaces
without implementations:

```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);HOAG030</NoWarn>
</PropertyGroup>
```

That is a supported target — a package carrying the contract for a client to consume, or one project
describing a service that another implements. It is a warning rather than an error for exactly that
reason. The `NoWarn` has to be written down rather than inferred, so that the ordinary case of
forgetting to write a handler is still reported.

### HOAG031 — handler implements no described service

A class carries `[Handler]` but nothing in its base list names a service the description declares.

```
'StrayImpl' carries [Handler] but its base list names no service the description declares -
it lists HandlerBase, IDisposable. It is registered against 'HandlerBase', which routes nothing.
```

Usually a spelling mismatch between the class and the generated interface, or a `[Handler]` left on a
class that is no longer a service.

Note what this is *not*. A handler declaring a base class is fine:

```csharp
[Handler]
public class CatalogHandler : HandlerBase, ICatalogService { }
```

C# requires the base class to come first, and the generator used to read the first base-list entry
and call it the service interface — so this registered `HandlerBase`, left `ICatalogService`
unimplemented, built clean, and every route on the service was dead. The base list is searched by
name now. HOAG031 fires only when *no* entry matches.

## Other diagnostics

| Id | Meaning |
|---|---|
| `HOAG001` | Error while generating the routing table. |
| `HOAG002` | The description could not be parsed; the build task's message is passed through. |
| `HOAG010` | A handler was skipped because a parameter type did not resolve. Other handlers are unaffected. |
| `HOAG020` | An operation declares a markup content type but names no view to render it. |
| `HOAT0xx` | OpenAPI build task. |
| `HSMT0xx` | Smithy build task. `HSMT011` is the CLI version pin, and is a warning locally by design. |
| `HRDR0xx`, `HRDV0xx`, `HRDW0xx` | Runtime, validation and web generators. |

## Content negotiation

Not a diagnostic, but the other thing a service states once rather than per operation.

An operation says what it produces — the `content:` keys of its success response, or
`[SupportedContentTypes("text/plain", "text/csv")]` on a hand-written handler. A request carrying no
`Accept`, or `*/*`, is answered with the first of them; a request naming media types gets its own
preference order honoured within the set.

What happens when a client's `Accept` shares nothing with that set is one answer for the whole
service:

```yaml
x-hardened-content-negotiation: lenient   # at the document root
```

```csharp
[ContentNegotiation(ContentNegotiationMode.Lenient)]   // on the entry point
```

`Strict` is the default and answers 406 with a body naming what the operation produces. `Lenient`
serializes with the default serializer anyway, which is what the framework did before this existed.

It is deliberately not per operation. A policy each operation restates is one that ends up applied
unevenly, and the operation that quietly differs is invisible. Nor is it derived from the document:
a 406 is the transport telling a client its `Accept` named nothing that exists, and unlike a 404 —
which is a domain outcome an operation may or may not report — there is nothing about it for an API
author to decide.

An operation that declares nothing negotiates exactly as it did before: every registered serializer
is a candidate.
