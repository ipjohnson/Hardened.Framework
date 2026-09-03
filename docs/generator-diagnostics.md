# Generator diagnostics

Every diagnostic the Hardened source generators raise, what causes it, and how to satisfy it.

Warnings become errors under `ContinuousIntegrationBuild`, so anything left unaddressed locally
fails CI. Each entry below names the `NoWarn` for the cases where the warning is describing
something you meant to do.

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

That is a supported target: a package carrying the contract for a client to consume, or one project
describing a service that another implements. It is a warning rather than an error for that reason,
and the `NoWarn` has to be written down so the ordinary case of forgetting to write a handler is
still reported.

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

C# requires the base class to come first, and the base list is searched by name, so HOAG031 fires
only when *no* entry matches a described service.

## Routing

### HRDR002 — unsupported route token syntax

A brace form the template accepts and Hardened does not compile, or a template that is not
well formed at all. Both compile and route today, and neither does what it was written to do.

| Written | What it actually means |
|---|---|
| `{id?}` | A mandatory segment named `id?`. The path it was meant to make optional does not match at all. |
| `{id=5}` | A mandatory segment named `id=5`. Nothing binds to `id`; give the C# parameter the default. |
| `{id:isbn}` | A constraint nothing declares. Declare it with `[RouteConstraint("isbn")]`, or drop it and let the parameter type refuse a bad value. |
| `{id` | A brace with no partner. The rest is matched as literal text, so the route answers nothing anyone sends. |
| `}` | The same, from the other end. |
| `{}`, `{:int}` | A token with no name, which binds nothing. |
| `{id}/x/{id}` | One name declared twice. Only one of the two segments a request sends could reach the parameter. |

Every offending token in a route is reported, and the handler is still emitted — the routing table
filters on unresolved parameters rather than on token syntax, so dropping it would bury this
diagnostic under CS0246s.

### HRDR005 — route token binds no parameter

A token the template compiles that no parameter binds, beside a parameter that fell onto the
request body because of it.

```
Route '/events/{eventid}' on 'EventController.Get' declares '{eventid}', which no parameter
binds. 'eventId' differs from it only by case, so it is read from the request body instead.
Route tokens bind by exact name: spell the token '{eventId}', or rename the parameter to
'eventid'.
```

Both halves are required. A token that binds nothing is not a mistake on its own — one declared in
a shared `[BasePath]` binds nothing on the handlers under it that do not need it. What makes it a
defect is the parameter that went somewhere else because of it: onto a body a `GET`, `HEAD`,
`OPTIONS` or `TRACE` does not carry, or — whatever the verb — onto a body when its name differs
from the token only by case. `DELETE` is not treated as bodyless: HTTP permits a body on one and
some APIs send it.

A described parameter binds by the wire name its contract declares, not by the C# identifier
allocated for it.

### HRDR006 — no routing generator is compiling this assembly's routes

A module declaring routes with nothing turning them into a routing table. It compiled without a
warning into an application that answered 404 to every route it declared, and the template's
`AGENTS.md` documented the trap without the build ever mentioning it.

```
'Depot.EventsController' declares routes and nothing in this project turns them into a routing
table, so every one of them answers 404 at run time. Reference Hardened.Web.SourceGenerator as
an analyzer, or drop the route attributes if this assembly is not meant to serve them.
```

The build cannot see a missing analyzer from inside the analyzer that is missing, so the question
is asked from one that is still there. `Hardened.Web.SourceGenerator` and
`Hardened.Idl.SourceGenerator` each declare `Hardened.Web.Generated.WebRoutingGeneratorMarker` as
post-initialization output — the one kind of generated source another generator can see — and
`Hardened.Library.SourceGenerator`, which every Hardened project references, reports its absence.

Triggered by a route declaration rather than by `[HardenedWebModule]`. That attribute means "bring
the web pipeline", which is a thing an assembly with no routes of its own legitimately does — the
framework's own `AspNetCoreRuntime` is one.

One report per assembly, not one per route: there is a single thing to fix.

### HRDR007 — service parameter binds from the request body

A handler parameter typed as its concrete class rather than the interface it is registered against.

```
Parameter 'store' of 'EventController.Handle' is read from the request body. A parameter that names
no route token and is not an interface binds from the body, and 'EventStore' has no constructor
that does not take one, so no body can be read into it. Mark 'store' [FromServices], or type it as
the interface it is registered against.
```

The convention is that a parameter naming no route token and typed as an interface comes from the
container, and anything else is the body. Typing a service as its implementing class therefore
makes it the body parameter, and the build fails as a `CS7036` inside `obj/**/generated/**` — in a
file the author did not write, naming neither the convention that decided this nor the parameter
whose meaning changed.

Reported narrowly, because the two shapes are otherwise indistinguishable: a body model is a
concrete class too. What separates them is that the deserializer cannot construct an interface, so
a type whose every public constructor takes one has no reading as a body at all. A body model with
a parameterless constructor, and an immutable one whose constructor takes its own data, are left
alone — as is a service the deserializer could construct, which arrives empty instead. Both fixes
in the message work; `[FromServices]` is the one that does not require the service to have an
interface.

## Validation

### HRDV004 — nested constraints are never reached

A generated validator descends into a member only where `[ValidateNested]` says to, so omitting it
switches off every constraint on the child type. Nothing said so at build time or at run time: the
trial's price tiers stored as 201 with an empty code and a negative price, from a model whose
constraints were all declared and all correct.

```
'CreateEvent.PriceTiers' does not declare [ValidateNested] and its element type 'PriceTier'
declares constraints, so none of them run and an invalid 'PriceTier' is accepted with no
error. Add [ValidateNested] to the property, or set <NoWarn>$(NoWarn);HRDV004</NoWarn> if the
skip is intended.
```

A warning rather than an error, because not descending is sometimes what was meant — a member
validated by a later step, a shared type whose constraints belong to another operation. The
`NoWarn` is what makes that choice deliberate rather than silent.

Reported on a property of a type that constrains something itself, whose member, array element,
collection element or dictionary value is a type declared in the same compilation carrying
constraints in either vocabulary. A type that constrains nothing is not a model this generator
validates — a data seed holding a list of records, a response case wrapping a body — and its
validator was never going to descend anywhere.

Descending by default is the better answer and is on the table for 1.0. It cannot be the answer in
a 0.x release: it changes what an existing application answers, from 201 to 400, on payloads it
accepts today.

## Other diagnostics

| Id | Meaning |
|---|---|
| `HOAG001` | Error while generating the routing table. |
| `HOAG002` | The description could not be parsed; the build task's message is passed through. |
| `HOAG010` | A handler was skipped because a parameter type did not resolve. Other handlers are unaffected. |
| `HOAG020` | An operation declares a markup content type but names no view to render it. |
| `HRDR0xx`, `HRDV0xx`, `HRDW0xx` | Runtime, validation and web generators. The ones with an entry have a section above. |

## Description build tasks (HOAT, HSMT)

The two description front ends share one task shell, one packaged-targets layout and one
model-diagnostics pass. Their codes share one numbering: a number means the same thing under
`HOAT` (OpenAPI) as under `HSMT` (Smithy), and a finding is always reported under the prefix of
the front end that read the document. Numbers that exist for one front end only leave a gap in
the other.

| Number | Meaning |
|---|---|
| `001` | The description file does not exist. |
| `002` | The description could not be parsed; the reader's reasons are included. |
| `003` | The description was declared as the wrong item kind. `HOAT003`: a spec left in `AdditionalFiles`, which the generator no longer reads. `HSMT003`: a `.smithy` IDL file pointed at `HardenedSmithyAst`, which takes a JSON AST. |
| `004` | A model or generated source the extract step should have written is missing. Delete the model directory and rebuild. |
| `005` | The targets file was imported before the specs were declared, so no generated source reached the compilation. Move the `<Import>` below the item group. |
| `006` | Warning. The reader parsed the document and had something to say about it, including what a degraded trait promises that the code does not enforce. Under `HSMT`, this is also where a prelude shape with no exact C# type is reported - `BigDecimal` becomes `decimal`, `BigInteger` becomes `long` - once per member, naming it. |
| `007` | A slice selected no operations, so nothing would be generated. |
| `008` | Warning. A slice removed a schema that is still referenced; the reference degrades to `JsonElement`. |
| `009` | Warning. The spec is sliced but its document is embedded whole, so the served description claims operations the application does not implement. |
| `010`–`014` | The Smithy CLI task; `HSMT` only. See below. |
| `015` | `HSMT` only. The model declares more than one `PublishUrl` or `UiUrl`. One model is one service at one address. |
| `016` | `UiUrl` without `PublishUrl`, so the page would have no document to render. |
| `017` | `SourceUrl` without `EmbedDocument`, so there is no source to serve. |
| `020`–`025` | The shared model-diagnostics pass. See below. |
| `026` | Warning. `$(HardenedResponseModel)` is `Standard`, the throws mode's name before 0.19.0. The mode selected is unchanged; write `Throws`. Reported once per project. |
| `027` | The description references something it does not declare. Part of the model-diagnostics pass; see below. |

### The Smithy CLI task (HSMT010–HSMT014)

| Id | Meaning |
|---|---|
| `HSMT010` | The Smithy CLI was not found. Install it, set `$(HardenedSmithyCliPath)`, or commit an AST and point `@(HardenedSmithyAst)` at it. |
| `HSMT011` | The CLI is not the pinned version. A warning locally by design, and an error under the pin, because a different CLI can produce a different AST from identical sources. |
| `HSMT012` | The CLI refused the model. One error per finding, at the file, line and column the CLI named; a report that does not parse is passed through whole. |
| `HSMT013` | Warning. What the CLI said without failing, with the same per-finding attribution. |
| `HSMT014` | The CLI exited cleanly but wrote no AST. Unlike `HSMT012`, the fix is not in a `.smithy` file. |

### The model-diagnostics pass (020–025)

Problems any description can state that would generate C# which does not compile, found before
anything is emitted so they are reported against the document rather than as compiler errors in a
generated file.

| Number | Meaning |
|---|---|
| `020` | Warning. A schema declares a property named like the schema itself, which C# forbids (CS0542). The member is renamed; the wire name is unchanged. |
| `021` | Warning. Two schemas generate one C# type name. Resolved automatically; rename one in the document to choose the names yourself. |
| `022` | Warning. A `oneOf` with no discriminator whose branches cannot all be told apart by shape. Payloads are matched by parsing into each branch; declare a discriminator to decide it in the document. |
| `023` | An `enum` declaring both string and numeric values, which no C# enum can carry. Declare one kind. |
| `024` | Warning. A declared keyword or trait the generator does not enforce, named with a representative location. Remove it, or enforce the rule in the handler. |
| `025` | Two error responses on one operation share one status, which would generate the same case type twice. Give them distinct statuses or merge the shapes. |
| `027` | A reference to something the description does not declare. |

#### 027 — a reference to something the description does not declare

```
'GET /events (200)' references '#/components/schemas/DoesNotExist', which the description
does not declare. Nothing is generated for it, so the member it types would be absent and a
response body would be dropped. Declare the schema, or point the reference at one that exists.
```

Fatal, unlike everything else in the block, because there is no answer to fall back on. A dangling
`$ref` in a response degraded the success case to a bodyless one, so a handler written against the
generated interface compiled and answered 200 with an empty body; the only errors were CS0246s a
hop away in application code that happened to name the missing model.

One report per place the reference is made — a document referencing a schema it dropped usually
does so from several, and each is a separate edit. The operation's flat response fields mirror its
primary success, so those count as one place rather than two.

Under `HSMT` this covers a target the model references and does not declare: an operation a service
binds, an operation's input or output, a member's target, and an error shape bound to an operation.
The Smithy CLI refuses all of these before the parser sees them, so only a committed AST reaches it.

**What it does not catch.** A `$ref` on a schema property is resolved by the reader before this
parser sees it, and an unresolvable one is discarded there — the property arrives with no reference,
no type and no shape, and becomes `JsonElement`. Nothing in the object model records that the
reference was ever made, and the reader reports nothing either. Catching that needs the raw
document rather than the object model.

### Renumbered in 0.18

Codes moved so that every number has one meaning per prefix, and so findings from a Smithy model
stop being reported as `HOAT`. If a `NoWarn` or a suppression names an old code, update it:
`HOAT003`→`HOAT020`, `HOAT005`→`HOAT021`, `HOAT010`→`HOAT022`, `HOAT011`→`HOAT023`,
`HOAT013`→`HOAT024`, `HSPEC010`→`025` under the front end's prefix, `010`→`016` and `011`→`017`
under both prefixes for the publish checks, the silent-success `HSMT012`→`HSMT014`, and the
multiple-`PublishUrl` `HSMT012`→`HSMT015`. The targets-layout `003`/`004`/`005` and the CLI codes
`HSMT010`/`HSMT011` kept their numbers.

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

It is one setting for the whole service, not per operation, and it is not derived from the document.

An operation that declares nothing negotiates as before: every registered serializer is a
candidate.
