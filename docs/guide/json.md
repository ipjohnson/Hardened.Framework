# JSON serialization

Hardened serializes JSON with `System.Text.Json`, and configures it by **registering an
`IJsonTypeInfoResolver`** — a source-generated `JsonSerializerContext`. That one registration is
read by every JSON serializer in the pipeline: the request body, the response body, and each item
of a streamed response.

```csharp
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(Todo))]
[JsonSerializable(typeof(NewTodo))]
public partial class AppJsonContext : JsonSerializerContext;
```

```csharp
[HardenedModule]
[HardenedWebModule]
public partial class AppLibrary : IServiceCollectionConfiguration {

    public void ConfigureServices(IServiceCollection services) {
        services.AddSingleton<IJsonTypeInfoResolver>(AppJsonContext.Default);
    }
}
```

Registered resolvers answer first, in registration order. Reflection covers whatever they do not
declare, on a host where reflection is available at all — so a context does not have to be complete
to be worth registering. The project templates ship this wiring already.

## Enum vocabulary

An enum's wire values are `[JsonEnumNaming]`, and **it governs the published document as well as the
wire**. The build writes a converter per enum, registers it for the JSON body and for the parameter
binder, and writes the same values into the `enum` array of the generated OpenAPI description — so
the contract a client generates against cannot disagree with the bytes the application produces.

The default is camelCase:

```csharp
public enum Priority { Low, InProgress }
```

```json
{ "priority": "inProgress" }
```

To choose something else, set it for the assembly, for one enum, or both — the enum wins:

```csharp
[assembly: JsonEnumNaming(EnumNaming.KebabCaseLower)]   // "low", "in-progress"

[JsonEnumNaming(EnumNaming.MemberName)]                  // opts out: "AB12", "CD34"
public enum LegacyCode { AB12, CD34 }
```

`EnumNaming` offers `MemberName`, `CamelCase`, `KebabCaseLower`, `SnakeCaseLower` and
`SnakeCaseUpper`. The attribute goes on the **assembly**, not the module class: the document is
written inside the syntax transform where the module's symbol is not reachable, and a naming the
document did not get is exactly the desynchronised contract this prevents. `AttributeTargets` is
narrow enough that a misplaced one is a compile error rather than a setting that reads as applied.

::: warning Decide the vocabulary before the first client
Changing an enum's wire values later breaks every consumer, and no compiler will tell you it
happened. The values are part of your API in the same way a property name is.
:::

Parameters bind through the same vocabulary. A path or query value is text rather than JSON, so it
never reaches a converter — without this, `?priority=in-progress` would be answered 400 by an
application whose body accepts exactly that, and any value that is not a valid C# identifier would
be unreachable as a parameter.

### What is left alone

- **`[Flags]` enums.** A flags value is a combination of members rather than one of them, so there
  is no member name to write and no single value to read back.
- **Enums from referenced frameworks.** A model graph reaches further than it looks — a property
  typed `Exception` pulls in `System.Reflection.MethodAttributes` — and renaming those would
  redefine a contract that is not yours. An enum in a shared model library opts in by that
  library declaring `[assembly: JsonEnumNaming]` of its own.
- **Aliased members.** Two members sharing a value keep the first declared, which is the one
  `Enum.ToString` picks.

## Adding your own converter

Five routes, and they do not all reach the same code.

| Route | Reaches | Use when |
|---|---|---|
| `[JsonEnumNaming]` | Body, parameters and the document | Any enum — see above |
| `[JsonConverter]` on the type or property | Everything | A non-enum type with one wire form |
| A `JsonSerializerContext` registered as `IJsonTypeInfoResolver` | Everything | The general answer — AOT-safe, and how the templates do it |
| `Converters` on `[JsonSourceGenerationOptions]` | Everything, via that context | A converter that should apply across the context's types |
| `Hardened.Requests.Runtime.Configuration.JsonSerializerConfiguration` | The wire only | You need to replace `JsonSerializerOptions` wholesale |

The last one takes the whole options object rather than a delta, so setting it drops
`JsonSerializerDefaults.Web` — camelCase, case-insensitive matching, `AllowReadingFromString` —
unless you rebuild all of it. Set both `SerializeOptions` and `DeSerializerOptions` or the
application writes what it will not read.

::: warning A converter in `Options.Converters` outranks `[JsonConverter]` on the type
`System.Text.Json` ranks options-level converters **above** the attribute. Adding a bare
`JsonStringEnumConverter` to the options therefore overrides converters generated from an OpenAPI or
Smithy contract, and writes the C# member name where the document declares something else —
`"ScienceFiction"` for a schema whose value is `science-fiction`, and a 400 reading it back. Attach
converters to types, or scope them to a context.
:::

## Contract-first applications

If your models come from an OpenAPI description or a Smithy model, `[JsonEnumNaming]` does not apply
and is not needed. Those enum values come from the description, which is the vocabulary by
definition, and the build already emits a converter carrying it plus the resolver that registers it.

## Native AOT

A published AOT application has no reflection fallback: every type on the wire must be declared in a
registered context, and one that is missing throws a `NotSupportedException` naming it. That is the
truthful failure — the same call succeeds on a JIT host by reflecting, which is what makes a missing
`[JsonSerializable]` line easy to ship.

**Do not use `JsonStringEnumConverter` at all in a Hardened application.** It writes the C# member
name rather than a wire value and never reaches the published document — `[JsonEnumNaming]` is the
supported route. If you reach for it anyway, the non-generic form builds a converter per enum at run
time, which is the one thing AOT cannot do:

```csharp
[JsonConverter(typeof(JsonStringEnumConverter))]        // works on a JIT host, fails when published
[JsonConverter(typeof(JsonStringEnumConverter<Priority>))]  // correct
```

The compiler reports this as `SYSLIB1034` wherever the enum is reachable from a
`JsonSerializerContext` — including when the non-generic form appears in `Converters` on
`[JsonSourceGenerationOptions]`. It is a warning, and a build with
`TreatWarningsAsErrors` turns it into the error it deserves to be. An enum reachable from **no**
context gets no diagnostic at all, which is the case worth being careful about: it works locally
and fails after publishing.

`[JsonEnumNaming]` has no such problem, and is not the same thing as `UseStringEnumConverter`. That
setting is AOT-safe — the generator has the enum at compile time — but it writes the C# member name
and does not reach the document, so an application using it publishes a description its own wire
format disagrees with. Leave it off and let the build write the converters.
