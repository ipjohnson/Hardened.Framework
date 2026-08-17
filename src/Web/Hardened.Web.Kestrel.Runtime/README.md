# Hardened.Web.Kestrel.Runtime

Hosts Hardened on Kestrel without the ASP.NET Core request pipeline.

```csharp
var services = new ServiceCollection();

services.AddLogging();
services.AddTransient<IHardenedEnvironment>(_ => new EnvironmentImpl(arguments: args));
new Application().PopulateServiceCollection(services);

await using var app = HardenedKestrelApplication.Create(
    services, kestrel => kestrel.ListenAnyIP(5000));

await app.RunAsync();
```

The module goes on the application alongside the usual ones:

```csharp
[HardenedModule]
[HardenedWebModule]
[KestrelRuntime]          // instead of [AspNetCoreRuntime]
public partial class Application { }
```

Nothing else changes. Controllers, filters, binding and the generated routing table are identical
to an ASP.NET-hosted Hardened application.

## What this actually does

`IServer.StartAsync` takes an `IHttpApplication<TContext>`, not a `RequestDelegate`.
`HostingApplication` — the piece that builds an `HttpContext`, opens the DI scope and raises the
hosting diagnostics — is only the default implementation of that interface, and nothing about
Kestrel requires it. This package supplies Hardened's own:

```
Kestrel → HostingApplication → HttpContext → ASP.NET middleware
        → HardenedMiddleware → AspNetExecutionContext → chain     (Hardened.Web.AspNetCore.Runtime)

Kestrel → HardenedHttpApplication → chain                         (this)
```

Kestrel itself is untouched — HTTP/1.1, HTTP/2, TLS, connection lifecycle and header parsing all
still come from it. `ListenOptions.UseHttps(...)` works normally, because TLS is connection-level
in Kestrel rather than part of the request pipeline.

## Measured difference

From `src/Benchmarks`, all six pipelines in a single run so the figures are directly comparable,
each driven from an identical feature collection with no server underneath:

| Route | On ASP.NET | **On Kestrel** | Saved | Minimal API | MVC |
|---|---|---|---|---|---|
| `GET /item` | 850 ns | **761 ns** | 10.5% | 762 ns | 1614 ns |
| `GET /item/{id}` | 904 ns | **831 ns** | 8.0% | 890 ns | 2671 ns |
| `GET /query` | 1206 ns | **962 ns** | 20.2% | 1006 ns | 3043 ns |
| `GET /binding/{id}` | 1176 ns | **1000 ns** | 14.9% | 1101 ns | 3721 ns |
| `POST /sum` | 1548 ns | **1469 ns** | 5.1% | 1623 ns | 4507 ns |

Routes carrying a query string save the most. `AspNetExecutionRequest.QueryString` reads
`HttpRequest.Query` — already parsed by ASP.NET — and then materialises a second `Dictionary` from
it with a `ToString()` per value. Here the raw query string is parsed once, directly.

Two things to keep in mind when reading the table. This host calls `IRequestLogger` on begin and
end and records `TotalRequestDuration`, matching the Lambda runtime; `AspNetCoreRequestHandler`
does none of that, so the ASP.NET column is doing slightly less work than the Kestrel one. And
none of these figures include sockets, HTTP parsing or framing, which Kestrel adds equally to
every hosted option — see `src/Benchmarks/README.md` for why that boundary was chosen.

## What you give up

This is additive. `Hardened.Web.AspNetCore.Runtime` remains the right choice when any of the
following matter — and the first one matters more often than teams expect.

- **ASP.NET's own hosting diagnostics.** The `Microsoft.AspNetCore.Hosting` `DiagnosticSource` and
  `EventSource` events are not raised, so instrumentation packages that subscribe to *those names*
  see nothing.

  Tracing itself does work, and has since the request pipeline began reporting spans. Hardened
  publishes its own `ActivitySource` and `Meter`, both named `Hardened.Requests` — a server span per
  request, parented on an inbound `traceparent`, tagged with route and status, plus a correlation id
  on every response. Point a collector at that name rather than at the ASP.NET one:

  ```csharp
  builder.AddSource("Hardened.Requests");   // and .AddMeter("Hardened.Requests")
  ```

  This bullet used to say distributed tracing did not work at all. It was written before the spans
  existed and outlived them, which is worth knowing because it sends people to
  `Hardened.Web.AspNetCore.Runtime` for a reason that no longer holds.
- **ASP.NET authentication and authorization** middleware.
- **General response compression.** Hardened's `GZipStaticContentCompressor` covers static content
  only, so dynamic responses have no equivalent.
- Static files, rate limiting, forwarded headers, HSTS, HTTPS redirection, health checks — the
  middleware ecosystem generally.
- `IHttpContextAccessor`, and anything in application code that depends on `HttpContext`.

Hardened supplies its own CORS, static content handling and filter pipeline, so those particular
overlaps are covered.

## Hosting inside a generic host

`HardenedKestrelApplication` owns the service provider it builds, which is the cheapest way to
start. To keep configuration binding, logging setup and coordinated shutdown from
`Microsoft.Extensions.Hosting`, register the hosted service instead:

```csharp
builder.Services.AddHardenedKestrel(kestrel => kestrel.ListenAnyIP(5000));
```

That keeps `Microsoft.Extensions.Hosting` and drops only `Microsoft.AspNetCore.Hosting`. Using
`ConfigureWebHostDefaults` would bring back `GenericWebHostService` and `HostingApplication` — the
things this package exists to avoid.

## Notes for anyone changing this

`FeatureExecutionResponse.Status` is deliberately backed by a nullable field rather than read from
`IHttpResponseFeature.StatusCode`. `ResourceNotFoundHandler` only supplies a 404 when it finds the
status still unset, and a server's response feature starts at 200 — reading it back makes every
unmatched route return an empty 200. There is an integration test for exactly this.

`HardenedHttpApplication.ProcessRequestAsync` must call `CompleteAsync` on the response. A response
that wrote no body never sends its headers otherwise, and the connection is left waiting on a
request the application already considers finished.
