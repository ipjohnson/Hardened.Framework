# Hardened benchmarks

Performance measurement for the Hardened framework, and a comparison against ASP.NET Core at the
one layer where that comparison is honest.

```bash
cd src/Benchmarks/Hardened.Benchmarks

dotnet run -c Release                                  # Hardened only (default)
dotnet run -c Release -- --aspnet                      # + ASP.NET Core comparison
dotnet run -c Release -- --verify                      # correctness check, no timing
dotnet run -c Release -- --job short                   # fast, noisy pass
dotnet run -c Release -- --filter "*Routing*"          # narrow to one set
```

Release is required — BenchmarkDotNet refuses to run an unoptimized build.

## Layout

| Project | Contents |
|---|---|
| `Hardened.Benchmarks.Contracts` | Request/response models and `ISumService`, shared by every implementation. No dependencies. |
| `Hardened.Benchmarks.Sut` | The Hardened application: controllers compiled by the real source generators. |
| `Hardened.Benchmarks.AspNetSut` | The ASP.NET baseline: the same five routes as MVC controllers and as minimal API endpoints. |
| `Hardened.Benchmarks` | The BenchmarkDotNet harness. |

The two SUTs are separate assemblies deliberately. MVC discovers controllers by the `*Controller`
naming convention and Hardened's controllers follow that convention too — in one assembly, MVC's
`ApplicationPartManager` would pick up Hardened's controllers and the baseline would stop being a
baseline. `Contracts` exists so neither SUT has to reference the other.

The harness is not named `*.Tests`, because `Directory.Build.props` injects `Microsoft.CodeCoverage`
into any project whose name ends that way, and `dotnet test` would sweep it up. A benchmark run is
neither of those things.

## Categories

| Category | Default | What it covers |
|---|---|---|
| `micro` | yes | Routing, serialization, string conversion, the filter chain, validation rules, pooling |
| `pipeline` | yes | A whole request through Hardened, both deployments, plus context construction |
| `startup` | yes | Application construction and first-request cost — the cold-start number |
| `aspnet` | **no** | Hardened against ASP.NET Core MVC and minimal APIs |

`--aspnet` adds the last one. Everything else passes straight through to BenchmarkDotNet.

## Why there is no server

Nothing here starts Kestrel. ASP.NET Core's request pipeline is a `RequestDelegate` over an
`HttpContext`, and Kestrel is only one of the things that can produce one — so both frameworks are
driven from a hand-built `DefaultHttpContext` instead, via `HttpContextFactory`.

Everything above the server boundary still runs: route matching, endpoint selection, parameter
binding, deserialization, handler invocation, serialization, response writes to a real stream.
What is excluded is socket I/O, HTTP parsing, header serialization and framing.

Two reasons, and the first is the important one:

- **Hardened is not an ASP.NET framework.** It runs on Lambda and on any other transport, where
  Kestrel's costs are never paid at all. Folding them in would measure one particular deployment
  rather than the pipeline.
- **They would bury the signal.** Measured pipeline work here is 600–1300 ns. A loopback socket
  round trip is tens of microseconds, and its run-to-run variance alone exceeds every difference
  these benchmarks exist to detect.

If you ever want the deployed end-to-end number for capacity planning, that is a different
question and wants a separate load-generator harness — not this one.

## What is measured, and what that leaves out

Both sides pay the same framing cost per request: create a DI scope, build the context object the
pipeline needs, run it, leave the serialized response in a stream. Keeping that boundary identical
is what makes the numbers comparable.

Known asymmetries, stated rather than hidden:

- **Context construction is included.** Something pays for it on every real request.
  `ContextConstructionBenchmarks` measures it separately so it can be subtracted rather than
  guessed at. Note that Kestrel pools and resets its feature collection across a connection while
  the harness builds a fresh one per request, so the ASP.NET figure slightly overstates what a
  Kestrel deployment would pay. Hardened allocates its context per request in production too, so
  its figure is what production pays.
- **Hosting diagnostics are excluded from both.** Driving the `RequestDelegate` directly skips
  ASP.NET's `HostingApplication` (Activity, EventSource, DiagnosticSource). Hardened's request
  logger is present but wired to a null logger. Neither side is charged for telemetry.
- **Serialization is an explicit axis, not a confound.** Hardened uses source-generated
  `JsonTypeInfo`; ASP.NET defaults to reflection-based System.Text.Json. Comparing only the
  defaults would report a serializer difference as a pipeline difference, so both ASP.NET flavors
  are run with and without a `JsonSerializerContext`.
- **Validation is measured at the rule level, not through the filter.** `ValidationFilter` is only
  emitted for routes generated from an OpenAPI specification, and the benchmark routes are
  hand-written controllers. Adding an OpenAPI-generated route would be the way to capture the
  filter's own overhead.

## Verification

Every run starts by executing each scenario once through each pipeline and checking that all of
them return 200, write a non-empty body, and **agree on that body**. `--verify` runs this alone;
`--no-verify` skips it.

This is not ceremony. The failure mode of a pipeline benchmark is silent: a route that does not
match returns 404 from the terminal delegate, writes nothing, and completes far faster than a route
that does. An unverified harness does not error — it reports an excellent number for doing nothing.

Two real bugs were caught this way while building it:

- `EndpointRoutingMiddleware` takes a `DiagnosticListener` that the generic host normally supplies.
  Without a host, building the ASP.NET pipeline throws.
- `AspNetCoreRequestHandler` only skips the fallthrough when `context.Response.HasStarted`, and the
  stock `HttpResponseFeature` hardcodes that to `false`. Hardened wrote a correct body and then had
  its status overwritten with 404 by the terminal delegate. `HttpContextFactory.TrackingResponseFeature`
  tracks the same signal Kestrel does — headers flush on first body write.

It caught a third later, and that one says something about the gate rather than about the harness.
From 23 August to 11 September 2026 the scheduled run failed every week at this step: `hardened-native`
and `hardened-features` answered the miss with no status at all. The SUT declares `[AspNetCoreRuntime]`
so one assembly can serve both measured deployments, and `AspNetCoreRuntimeLibrary` replaces
`IResourceNotFoundHandler` unconditionally with the one that leaves the status unset for ASP.NET's own
terminal 404 to answer. `HardenedAppFactory.BuildProvider` puts the terminal handler back for the two
harnesses that own the whole response.

The gate did its job. Nobody read it, because a scheduled workflow failing is quieter than a build
failing, which is the same observation the comment at the top of `benchmarks.yaml` makes about the
credential it lost. If these numbers matter between Sundays, run `--verify` locally.

## The allocation gate

`allocation.yaml` runs on every pull request. It runs the verification pass, measures
`PipelineBenchmarks` and `StringConversionBenchmarks` at `--job short`, and fails the build when a
benchmark allocates more per operation than `allocation-baseline.json` records for it.

It gates bytes and never nanoseconds, which is the distinction the comment at the top of
`benchmarks.yaml` draws. `BytesAllocatedPerOperation` is a count rather than a duration, so it does
not move with how fast the machine is or what the neighbouring container is doing. It is also the
column that carried the signal the last time this mattered: the three allocations removed in #335
came to 56 bytes per request and every one of the fifteen pipeline measurements moved by exactly
the predicted amount, while the timings for that same change moved by less than the run-to-run
variance on a quiet laptop running the full default job.

A ceiling, not a ratchet. An improvement is printed and the ceiling is left where it is, for the
reason `scripts/coverage-gate.py` gives at length about floors. Move one deliberately:

```bash
gh run download <run-id> -n allocation-results -D /tmp/alloc
python3 scripts/allocation-gate.py --results /tmp/alloc/results --update
```

Take the reading from a CI run rather than a local one, and never run `--update` in CI. The results
artifact is uploaded on a failed run too, which is what makes a red run usable for re-baselining.

If every entry moves by the same handful of bytes in the same direction on a branch that changed no
pipeline code, read the runtime version in the run summary before reading the diff. A patch bump to
.NET 8 can change what the BCL allocates underneath the pipeline. That is why the workflow names
both SDKs exactly rather than floating them.

## Adding a scenario

Add it to `Scenarios` in `Infrastructure/RequestScenario.cs`, then implement the same route in
`Hardened.Benchmarks.Sut` **and** in both ASP.NET flavors in `Hardened.Benchmarks.AspNetSut`. The
verification pass will tell you if the three disagree.
