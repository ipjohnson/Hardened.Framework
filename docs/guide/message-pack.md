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

## What is not covered

**A `oneOf` schema.** The generated choice type has a JSON converter written for it and no
MessagePack formatter.

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
