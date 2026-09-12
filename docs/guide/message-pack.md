# MessagePack

An operation can answer MessagePack as well as JSON, and the client's `Accept` decides. Import
`Hardened.Requests.Serializers.MessagePack`, name the media type on the operation, and annotate the
model.

```csharp
[HardenedModule]
[HardenedWebModule]
[MessagePackSerializerLibrary]
public partial class AppLibrary;

[MessagePackObject]
public partial record Reading([property: Key(0)] string Sensor, [property: Key(1)] int Value);

[Get("/readings/{id}")]
[Produces(KnownContentType.Json, MessagePackContentType.Value)]
public Reading Get(int id) => new("sensor-" + id, id * 3);
```

```
GET /readings/3
Accept: application/x-msgpack
```

The media type is `application/x-msgpack`, and that is the only spelling. `application/msgpack` and
`application/vnd.msgpack` are both in use; supporting more than one would not work, because the
compile-time binding resolves a serializer by an exact lookup on its own content type.

Importing the package changes what nothing answers. The serializer registers under
`application/x-msgpack` and is asked only where an operation declares it, so a service that imports
the package and declares nothing still answers JSON everywhere. It also installs formatters for
Hardened's own response bodies — see [below](#hardened-s-own-response-bodies).

JSON is listed first above, so a client expressing no preference gets JSON. The order is the
operation's.

## The request side is not negotiated

A reader is chosen by the inbound `Content-Type`, whether or not the operation names the media type.
So a handler with a body reads MessagePack from a client that sends it and JSON from one that does
not, with nothing declared.

## Annotating the model

MessagePack writes a formatter for a type at build, from `[MessagePackObject]` and a `partial`
declaration. There is no reflection fallback in the resolver chain this package installs, which is
deliberate: a model nobody annotated would serialize on a developer's machine and throw
`PlatformNotSupportedException` after a NativeAOT publish, and the one configuration where the
mistake is invisible must not be the one you build in. A model that is not annotated fails the same
way everywhere.

Two ways to identify a member on the wire.

**By name.** `[MessagePackObject(true)]` is `keyAsPropertyName`. Pin the name the document
publishes rather than leaving the C# member's:

```csharp
[MessagePackObject(true)]
public partial record Reading([property: Key("sensor")] string Sensor);
```

Without the `[Key]` the wire carries `Sensor`, and a client generated from a document that says
`sensor` reads nothing.

**By index.** `[MessagePackObject]` and `[Key(n)]`. Smaller on the wire, and the identity survives a
rename. The numbers are the contract: renaming `Sensor` is free, renumbering it breaks every client
generated before the change.

## The index reaches the document

A key is no use to a client generator that cannot see it, so Hardened publishes it as
`x-message-pack-index` on the property's schema:

```json
"Reading": {
  "type": "object",
  "properties": {
    "sensor": { "type": "string", "x-message-pack-index": 0 },
    "value":  { "type": "integer", "format": "int32", "x-message-pack-index": 1 }
  }
}
```

Code-first, that is read off the `[Key(n)]` you wrote. Hardened does not add the attribute and does
not invent an index: a source generator cannot add an attribute to a member of a type it did not
declare, and an index chosen for the document alone would describe a wire format the server does not
speak.

## Specification-first

A contract states the index, and the build writes the attributes.

```yaml
components:
  schemas:
    Reading:
      type: object
      properties:
        sensor:
          type: string
          x-message-pack-index: 0
        value:
          type: integer
          format: int32
          x-message-pack-index: 1
```

```xml
<PropertyGroup>
  <HardenedSerializer>MessagePackKeyed</HardenedSerializer>
</PropertyGroup>
```

`$(HardenedSerializer)` takes `Json`, `MessagePackNamed` or `MessagePackKeyed`, and absent means
`Json`. A build property rather than an attribute, because the models are written by a task that
runs before the compiler and a task that runs first cannot read one.

Under `MessagePackNamed` nothing has to be stated: the property names are already in the contract,
and the build pins each one with `[Key("...")]`.

Under `MessagePackKeyed` every member has to state an index, and a member that does not is a build
error naming the member and the next free index:

```
HOAT033: Property 'note' of schema 'Reading' declares no x-message-pack-index, and
$(HardenedSerializer) is MessagePackKeyed. Nothing assigns one, because an index this build
chose would move when a property is added above it. Write "x-message-pack-index: 8" on the
property.
```

**Nothing assigns an index.** That is the design rather than a gap. An index taken from declaration
order moves the first time a property is inserted above it, and the document diff that moved it
reads as an addition — every client generated before it then reads the wrong member, with nothing
failing anywhere. An index hashed from the name is stable and collides. A keyed wire format is worth
having because the key is stable, so the only safe answer is the one the contract states.

Two members at one index is the same error, for the same reason.

A member bound to a response header is skipped. It leaves as a header rather than in the body, so it
is excluded from the payload with `[IgnoreMember]` rather than keyed.

The index round-trips: the parser reads it on the way in and the generator writes it back out, so a
service generated from a published document gets the indices the original contract declared.

## Generating a client

The document carries the key; a generator has to be taught to read it. For Refitter that is two
Liquid files, which the project templates ship:

```json
{
  "openApiPath": "../App/openapi/App.json",
  "customTemplateDirectory": "templates"
}
```

`Class.Annotations.liquid` puts `[MessagePackObject]` on each generated contract and
`Class.Property.Annotations.liquid` puts `[Key]` on each member, reading
`property.ExtensionData["x-message-pack-index"]`. NJsonSchema ships both templates empty and renders
them into every class and property, so overriding them adds attributes and changes nothing else; a
custom template directory overrides one file at a time and falls back to the embedded template for
every name it does not find.

**The attributes alone do not change the wire.** Refit picks its format from
`RefitSettings.ContentSerializer`, and the default is System.Text.Json — so a generated client with
every key in the right place still sends and receives JSON until it is given one:

```csharp
var client = RestService.For<ITodosClient>(
    http, new RefitSettings { ContentSerializer = new MessagePackContentSerializer() });
```

The template writes that serializer into the client project — about fifty lines over `Refit` and
`MessagePack`, and no Hardened package, because every body it reads is one of its own generated
contracts. It is yours to edit: adding LZ4 compression, or a resolver for a type you wrote by hand,
happens there. `tests/App.Tests/MessagePackClientFactory.cs` hands it to the client the tests drive, so every
client test in a scaffolded project runs over MessagePack — the typed 404, 409 and 400 bodies
included.

**The order in the contract is the client's preference.** Refitter reads an operation's media types
in document order and pins them on the interface as
`[Headers("Accept: application/json, application/x-msgpack")]`. That header is on the request, and a
header on the request wins over `HttpClient.DefaultRequestHeaders` — so a client cannot change its
mind by setting a default, and overrides `Accept` per request through a `DelegatingHandler`.
Whichever representation you want your clients to reach for by default, declare it first.

### Refit cannot read a binary error body

`ApiException` carries the response content as a `string`. Refit reads an error response with
`ReadAsStringAsync` and disposes the content, and `GetContentAsAsync<T>()` re-wraps that string for
your serializer — so bytes that are not valid UTF-8 are U+FFFD before anything is asked to parse
them. Request bodies and success bodies are unaffected; those reach the serializer as real
`HttpContent`.

Nothing on the client can recover them, so the answer is on the service: send text.

```csharp
[HardenedModule]
[HardenedWebModule]
[MessagePackSerializerLibrary]
[JsonErrorBodies]
public partial class AppLibrary;
```

Every failed request then answers JSON whatever it negotiated, and successes are untouched. A
description says the same with `x-hardened-error-bodies: json` at its root.

It is one answer for the whole service, for the reason `[ContentNegotiation]` is: a policy that has
to be repeated is one that ends up applied unevenly. And the published document follows — an error
response declares `application/json` and nothing else — so a generated client reads what it is
actually sent.

The client side is then one serializer that reads both, which is what a client facing a negotiating
service should be anyway. The template's `MessagePackContentSerializer` dispatches on the payload:
a body starting `{` or `[` is JSON, everything else is MessagePack. It sniffs rather than reading
the content type because `GetContentAsAsync` throws that away, and it is safe for the bodies in
play — a MessagePack map header is `0x80`–`0x8F`, `0xDE` or `0xDF`, and an array `0x90`–`0x9F`,
`0xDC` or `0xDD`.

Two more things Refitter decides for you. It pins the document's media types on the interface as
`[Headers("Accept: ...", "Content-Type: ...")]`, in document order, and a header on the request
wins over `HttpClient.DefaultRequestHeaders` — so a client preferring the other representation
overrides both through a `DelegatingHandler`. Leave `Content-Type` alone and the service reads a
MessagePack body as JSON and answers 400.

Kiota is not supported and not for want of an extension point. Its models carry no serialization
attributes at all — they implement `IParsable` with hand-written `Serialize` and
`GetFieldDeserializers` — so an attribute would have nothing to act on. Supporting MessagePack
through Kiota means writing a Kiota serialization writer.

## Scaffolding

```bash
dotnet new hardened-web -n Sample --serializer message-pack-keyed --contract openapi --client refit
```

`--serializer` takes `json`, `message-pack-named` and `message-pack-keyed`. It wires the package,
the module attribute, the media types on the contract and the two Liquid templates.

It cannot be combined with `--contract smithy`. A Smithy model states its wire format through its
protocol trait, and there is no MessagePack protocol to state — so the contract has nowhere to say
that an operation answers `application/x-msgpack`, and nowhere to state a member's index.

## Hardened's own response bodies

They are covered, so an operation can declare MessagePack whatever it answers with.

`ExceptionResponseSerializer` negotiates the error body through the same locator the success goes
through, so an operation declaring MessagePack answers its refusals as MessagePack — and a declared
status whose body is a framework type does the same. `HardenedFormatterResolver` answers for the two
error envelopes, `ErrorModel` and `RequestValidationError`, and for every built-in response type
that reaches a serializer:

```csharp
[Get("/readings/{id}")]
[Produces(KnownContentType.Json, MessagePackContentType.Value)]
public Response<Reading, NotFound> Get(int id) => ...   // the 404 answers MessagePack too
```

None of those types can carry `[MessagePackObject]` themselves — they live in
`Hardened.Requests.Abstract`, `.Runtime` and `Hardened.Web.Runtime`, which every application
references and none of which is taking a MessagePack dependency for them. The formatters are
written by hand in the serializer package instead, with the keys and the member order the JSON
representation uses, so a caller switching on `type` reads the same body either way.

**Named keys, under the keyed mode too.** The document publishes no `x-message-pack-index` for a
framework type, so a client generated from it keys these by name whichever mode it is in. Writing
integers would disagree with every such client on every error body.

Most of the built-in types never reach a serializer at all, which is why this is a short list. The
generated dispatch assigns `ICarriesResponseBody.Body` rather than the wrapper, so `Created<T>`,
`NotFound<T>` and the other generic wrappers send their payload; a type with `HasBody => false` —
`NoContent`, `Accepted`, `NotModified` — writes nothing.

A .NET client reading one of these bodies composes options with the same resolver:

```csharp
var options = MessagePackSerializerOptions.Standard
    .WithResolver(CompositeResolver.Create(
        [], [HardenedFormatterResolver.Instance, StandardResolver.Instance]));
```

## A `oneOf` is JSON only

MessagePack does not carry a choice, and will not. The JSON side resolves one with a generated
converter that reads a discriminator out of the payload before it knows which type to build;
MessagePack binds a formatter to a static type at build and has no equivalent step, and a binary
format that carries no discriminator of its own is the wrong place to put a choice.

So a contract declaring a `oneOf` generates and serializes it as JSON exactly as before, and the
build says the other representation is short of it:

```
HOAT034: Schema 'Payload' is a oneOf, and MessagePack does not carry one - a choice is
resolved from a discriminator in the payload, which is a JSON-only shape here. It is
generated and serialized as JSON as before; an operation that answers it as
application/x-msgpack fails at the response. Declare that operation as JSON only.
```

A warning rather than an error, because a contract is free to declare a choice that no MessagePack
operation ever answers with.

## What is not covered

**`Vary: Accept` and response caching across two representations.** A cached answer is stored under
the request rather than under the representation it was negotiated into.

## Answering for a type the generator skipped

Register an `IFormatterResolver` with the container. Registered resolvers are composed ahead of the
package's own chain, so they are asked first:

```csharp
public void ConfigureServices(IServiceCollection services) {
    services.AddSingleton<IFormatterResolver>(MyResolver.Instance);
}
```

`MessagePackSerializerConfiguration` is a `[ConfigurationModel]`, so the options themselves are
amended the way any model is — see [Configuration](/guide/configuration). Its `OptionsProvider` is a
factory over the service provider, because a resolver worth configuring is usually one built from
something in the container.

## Where to go next

- [Content negotiation](/guide/content-negotiation) — how `Accept` is matched, and what a 406 means
- [JSON serialization](/guide/json) — the representation every operation still answers
- [Clients](/guide/clients) — generating one from the published document
