# Template plan

Templates are named for the **workload**. Everything else — host, contract source, optional
features — is an option on the template. No template name contains a host.

## Why that factoring is right, and the stronger version of it

Host in the name explodes: two workloads by five hosts by three contract sources is thirty
templates, and adding Azure adds six more. Host as an option adds one directory.

But there is a stronger claim available, and it is the framework's own:

> Controllers, filters, binding and the generated routing table are identical to an
> ASP.NET-hosted Hardened application.
> — `Hardened.Web.Kestrel.Runtime/README.md`

If that is true, then **the host affects exactly one project**. Not a flag threaded through a
generated solution — one directory that gets swapped, with everything else bit-for-bit the same
whether you deploy to Kestrel, Lambda, or eventually Azure Functions.

That reframes the template suite from a convenience into a proof. If a generated
`--host kestrel` and a generated `--host aws-lambda` differ anywhere outside the host project,
either the template is wrong or the claim is. CI can assert it (below), and once it does, adding
Azure means adding a host directory and watching the assertion hold.

## Layout

Three projects, the same in every combination:

```
Foo/
  src/Foo/            implementation library — routes or handlers, services, contract
  src/Foo.Host/       the ONLY host-specific project
  tests/Foo.Tests/
```

The application module lives in the host, because naming the runtime is what a host does. The
library module — `[HardenedModule] [HardenedWebModule] [BasePath("/foo")]` — lives in the library
and knows nothing about where it runs.

**Tests target the library module, not the host's application module.**
`[assembly: HardenedTestEntryPoint(typeof(FooLibrary))]`. `ITestWebApp` drives the real pipeline
with no host at all, so a test suite that names the host is coupled to a deployment target for no
reason. The trade is that host-level composition — configuration amendments, services the host
registers — is not covered by the default test; that is worth a separate host test in a real
application and is not worth it in a template.

This is also what makes the CI assertion possible: the library and the tests are byte-identical
across hosts, and only `src/Foo.Host/` differs.

## Templates

| Short name | Workload | Phase |
|---|---|---|
| `hardened-web` | HTTP routes | 1 |
| `hardened-function` | Event handlers | 2 |
| `hardened-library` | A module in its own assembly, no host — added to an existing solution | 2 |

`hardened-library` is worth its own template rather than a flag: it is the shape the framework's
whole composition story is about, it has no host project at all, and it is where the cross-module
links defect lived. Adding a library to a solution you already have is a real thing people do.

## Options

### hardened-web

| Option | Values | Default | Affects |
|---|---|---|---|
| `--host` | `kestrel`, `aspnet`, `aws-lambda` | `kestrel` | `src/Foo.Host/` only |
| `--contract` | `code`, `openapi`, `smithy` | `code` | one file + one csproj block in `src/Foo/` |
| `--openapi-doc` | on/off | **on** | one attribute on the host module |
| `--openapi-ui` | on/off | **on** | one attribute on the host module |
| `--views` | on/off | off | RazorBlade packages, a view, an `[Output<T>]` route |
| `--static-content` | on/off | off | package, `wwwroot`, the MSBuild item |

`--contract` does not apply to `hardened-function`: event handlers have no routes, so there is no
document to derive them from.

Smithy is not a third programming model. Both OpenAPI and Smithy normalize through
`Hardened.Idl.*` into the same generator, so a Smithy handler and an OpenAPI handler are the same
C# — `[Handler]` on a class implementing a generated interface. Three contract sources, two
shapes of code.

### hardened-function

| Option | Values | Default |
|---|---|---|
| `--host` | `aws-lambda` | `aws-lambda` |
| `--trigger` | `invoke`, `sqs`, `ddb-stream` | `invoke` |

`--trigger` values are host-scoped in a way `--host` values are not: SQS is AWS, Pub/Sub would be
GCP. `template.json` choice symbols cannot express "valid only when `--host` is X", so an invalid
pair has to fail somewhere. Fail it in a post-action with a message naming the valid pairs, and
keep the verification matrix to real combinations.

## The default route

The same route, the same shape, in every variant:

```
GET /greeting/{name}   ->   { "message": "Hello, name" }
```

A record rather than a bare string, so the generated OpenAPI document has a schema in it and the
UI shows something on first load. Identical across `code`, `openapi` and `smithy`, which is what
lets the generated test be identical too — and the identical test is half of the CI assertion.

## Defaults on, opt out

Publish, UI, and a test, all on. One caveat.

`HardenedOpenApiUi` carries `Path`, `Title`, `DocumentPath`, `ScriptUrl`, `ScriptIntegrity` — and
nothing about where it is allowed to run. Default-on therefore means every generated service ships
a documentation page in production that fetches a script from a CDN at runtime. Swashbuckle
defaults to development-only for exactly this reason, and `IStaticContentConfiguration` already
carries a `Requirement` for the analogous problem.

Keep it on — it is the right first run — and add an environment gate to the module rather than
relying on people noticing an attribute. That is a framework change, not a template one, and it
should land before or with the template.

## What blocks what

**`--host aws-lambda` cannot ship yet.** `Hardened.Amz.Web.Lambda.Runtime 0.6.0-rc1000` depends on
`Hardened.Web.Runtime 0.6.0-rc1000`; the framework is at `0.8.0-rc1000`. A template pinning both
would either put the whole application two release lines back or mix build lines, which
`reference/packages` already says is unsupported — and already narrates going wrong:

> Hardened.Amz tracked a framework three release lines old that way, with a green build throughout.

The template is the forcing function. Verification that restores a generated Lambda project fails
on the version conflict, where a green build did not. Ship `hardened-web --host kestrel|aspnet`
first; add `aws-lambda` when Amz tracks the line.

**Azure and GCP do not exist yet.** The point of this factoring is that they are additive: a host
directory, one value in a choice symbol, one row in the verification matrix. Nothing about the
implementation library or the tests changes, and the CI assertion says so out loud.

## Packaging and distribution

One NuGet package, `Hardened.Templates`, containing every template:

```
content/hardened-web/.template.config/template.json
content/hardened-function/.template.config/template.json
content/hardened-library/.template.config/template.json
```

```xml
<PackageId>Hardened.Templates</PackageId>
<PackageType>Template</PackageType>
<TargetFramework>netstandard2.0</TargetFramework>
<IncludeContentInPack>true</IncludeContentInPack>
<IncludeBuildOutput>false</IncludeBuildOutput>
<ContentTargetFolders>content</ContentTargetFolders>
<NoWarn>$(NoWarn);NU5128</NoWarn>
```

nuget.org is the central repo and the CLI already knows how to reach it:

```bash
dotnet new install Hardened.Templates
dotnet new hardened-web -n Foo
```

`dotnet new search hardened` queries nuget.org directly and returns nothing today, so the name is
free. Local install from a packed branch build works the same way.

### The version-pinning trap

The generated csproj must pin `Hardened.*` at a version, and a hardcoded one is exactly the drift
that left the RazorBlade install snippet four release lines stale.

One substitution point: emit a `Directory.Packages.props` with a single `<HardenedVersion>`, and
stamp it from `$(Version)` at pack. Template and framework ship from the same release, so it is
exact — and the generated project gets central package management, which the docs already
recommend.

## CI

Two jobs, and the second is the one that matters.

**Verify, per variant.** Install the *packed* nupkg, `dotnet new`, restore, build, test, run,
`curl` the default route. Not a project reference — the artifact users get. A template built in
the solution with `ProjectReference`s proves nothing about packaging, which is where the
0.8.0-rc1000 quickstart broke.

**Host-independence assertion.** Generate the same project once per host and diff. `src/Foo/` and
`tests/Foo.Tests/` must be byte-identical; only `src/Foo.Host/` may differ. This is the framework's
central claim, checked mechanically, and it is the thing that will keep Azure and GCP honest when
they arrive.

Generate the verification matrix from the choice symbol's values so a new host cannot be added without
being tested.

## Sequencing

1. **`hardened-web`, hosts `kestrel` and `aspnet`, contract `code`.** Lift from
   `hardened-adopter/starter/`, which is verified serving. Defaults on: document, UI, test.
2. **Both CI jobs.** Before more variants, so nothing lands untested.
3. **`--contract openapi` and `--contract smithy`.** One file and one csproj block each.
4. **Environment gate on `HardenedOpenApiUi`.** Framework change; unblocks defaulting the UI on
   without shipping it to production.
5. **`hardened-library`.**
6. **Amz onto the framework's release line**, then `--host aws-lambda`, then `hardened-function`.
7. **Azure and GCP** — by then, one directory each.
