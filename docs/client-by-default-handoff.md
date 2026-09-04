# Client by Default: handoff notes

What the implementation of the Client by Default work order found and did not fix, and the
decisions the order left to the implementer. The design and the order are the authority; this
records what happened when they met the code.

## Decisions taken on the order's defaults

- The exported document is committed and every build rewrites it. The task compares content and
  leaves an identical file alone, so a second build touches nothing; the README's CI section is
  `dotnet build` then `git diff --exit-code src/<Name>/openapi`.
- The client project is packable, with a `PackageId`, and nothing pushes it.
- Warnings as errors stayed on for the client project. Kiota 1.34.1's output compiles warning-free
  on net8.0 under the template's analyzers; the one warning Kiota prints itself (no `servers`
  entry in a code-first document) is kept out of the build's count with
  `IgnoreStandardErrorWarningFormat`, so it is visible and not fatal.
- `net8.0` only for the client.
- The client project goes into both solution files through the template engine's own conditional
  syntax for `.sln` files, which a probe showed it evaluates cleanly, rather than through the
  add-projects post-action. One mechanism for every conditional in the template, and nothing that
  can print manual instructions instead of acting.
- The Kiota pins are tool 1.34.1 and bundle 2.0.0. The design named 2.1.0; `kiota info` for
  1.34.1 reports 2.0.0, and the pair check holds the pins to what the tool reports. 1.35.0 was
  current on nuget.org when this was written and was not taken, on the order's "pin what the
  verify script passes on".

## Where the code departed from the order's placement

- The served literal was already emitted under one member name for both front ends,
  `OpenApiDocumentGZip` on the entry point, because the spec-first generator calls the same
  `OpenApiDocumentSource.Write`. What A1 added is the fixed type: the getter now lives on a static
  class named `OpenApiDocument` nested in the entry point, as `{EntryPoint}.OpenApiDocument.GZip`.
  Nested rather than at a fixed full name so two entry points in one compilation each carry their
  own and the export can report that (019) rather than the compiler reporting a duplicate type.
- The shared targets file lives in the export task's own project,
  `src/SourceGenerators/Hardened.OpenApiDocument.BuildTask/Package/Hardened.OpenApiDocument.targets`,
  not under `Hardened.SourceGenerator/Package/`, which is the source-only package's build folder
  and is not what the three generator packages pack. Each generator package carries the file in
  its `build/` and imports it from its own targets under a guard, so a project referencing two of
  them imports it once. In this repository, where the fixtures import the generator targets from
  the source tree, a fallback path finds the shared file in the task's project.
- The Web integration application carries a generated client, in
  `Hardened.IntegrationTests.WebApp.SUT.Client`: the template's client project with the names
  changed, generating from the tracked `openapi/Application.json` on every build, and
  `GeneratedClientTests` in the SUT's test project drive it through the pipeline - bodies, path and
  query parameters, a declared 201 and 204 read from `LastResponse`, the typed
  `RequestValidationError` for a declared 422 and for the default 400, a bare `ApiException` for
  an undeclared 401, credentials as attributes, and a mock reached through the generated client.
  The order kept generators out of Hardened *packages*; a test fixture is not one, and without it
  the only proof that a Hardened document generates a working client lived in the templates job.
  The cost is a second Kiota pin pair, which `scripts/verify-templates.sh` holds to the template's.
- The Web integration application exports at the served version, 3.2.0, so its
  `ExportedDocumentTests` compares byte for byte. The order had the SUT set
  `HardenedOpenApiOutputVersion=3.1.0` for the lint, which would have broken that comparison; CI
  instead invokes the export target once more on the same project with the two properties as
  global properties and lints that second file. One target on one project, so the property
  cannot reach a referenced project and fail it for carrying no document.
- Diagnostic numbers: `018` and `019` were the next free numbers in the shared HOAT/HSMT range,
  and the remaining three took `028` to `030` after the model-diagnostics pass, since `025` is
  retired and stays so. The same numbers report under `HRDOA` for code-first.
- The 3.0.0 lowering rewrites two spellings the order did not mention, a numeric
  `exclusiveMinimum`/`exclusiveMaximum` and a `type` array with `"null"`, into the 3.0 forms. A
  3.0 reader refuses the 2020-12 spellings, and the generator itself writes the bounds the 3.0 way
  under a 3.0 banner; the nullable type array the generator writes regardless of version.
- `LastResponse` is keyed on `TestContext.Current.Test` alone. The order's fallback to the test
  case being prepared turned out to be dead: the DependencyModules runner builds the container
  and resolves parameters inside xUnit's test-method stage, where the context has the method and
  neither a test nor a test case, so a request answered there is not kept and a read there says
  no test is running. The test body, where assertions live, always has the test.
- The credential attributes on a parameter are `ITestParameterValueProvider`s, which is how two
  parameters of one client type carry two credentials without a new hook in the runner: the
  attributed parameter is built by its attribute, the bare one resolves the instance
  `[WebTesting]` registered with the method's credential.
- `TestGrantsPrincipalSource` is registered with `TryAddEnumerable` rather than `TryAdd`: the
  middleware asks sources in registration order and a null answer falls through, so the test
  source beside an application's own leaves the application's authentication untouched, while
  `TryAdd` would have left the attributes silently inert in any application that registers a
  source of its own.
- The scaffolded client tests reach Kiota's models through an alias, `ClientModels`, rather than
  a `using` for `Hardened1.Client.Models`. The models are named after the schemas, the schemas are
  named after the application's own types, and a test in `Hardened1.Tests` resolves a bare
  `NewTodo` to the application's record before any `using`. `Generated` was the first name tried
  and is a namespace the build already declares.
- A test parameter type with neither client route is registered to fail naming both routes only
  when the container could not construct it on its own; a type the container can build is left to
  it, so nothing that resolved before stops resolving. A later `TryAdd` of such a type by another
  setup attribute would be skipped in favour of the failing registration; nothing in the
  repository does that.

## Discovered and not fixed

- ~~A Smithy service in throws mode answers a declared 404 by returning null, and the runtime
  writes no body at all.~~ **Fixed.** `SchemaModel.IsErrorShape` carries the `@error` trait through
  to `DefaultErrorBody`, which fills a required `message` with the status's reason phrase - the
  same act as filling 7807's `title`, and licensed by the same thing: Smithy gives that member one
  meaning. Keyed on the trait rather than the member's name, because `{ message: string }` is an
  ordinary shape. The scaffolded test is no longer skipped under
  `--contract smithy --response-model throws`, and `PetStoreRoutingTests` covers the same shape
  in-repo.
- An OpenAPI service in throws mode answers the same null return with the document's `Problem` and
  no `detail`, which deserializes. The generated client throws the typed
  `Problem` either way; only its detail differs between the modes, and the scaffolded test asserts
  the detail under the declared models alone. **Settled as intended**: `status` and `title` are
  facts about the status code and are filled; `detail` is a fact about this occurrence, which the
  framework does not have. Inventing one would put words in the handler's mouth, and a handler with
  something to say throws instead. ASP.NET Core answers a bare 404 with no body at all.

- The Smithy integration application's served document repeats the `post` key under `/`, because
  its bank service speaks the AWS JSON protocol and every operation is `POST /` told apart by a
  header. That is the protocol's shape and was recorded as such in the 0.18 trial; the export
  carries it faithfully in both formats, but Microsoft.OpenApi's reader refuses the document, so
  the Smithy application is excluded from the JSON-versus-YAML parse comparison with the reason in
  the test. A Kiota client cannot be generated from that document either.

  Not fixable as OpenAPI: a Path Item is a map keyed by method and holds one `post`. Synthesising
  a path per operation would describe routes the service does not serve. **The export says so now**
  under `031`, a warning naming the path, rather than leaving an unusable file to be discovered by
  whatever was pointed at it.
- `RequestPathDecoder` documents `a%zz`, `a%` and `a%2` as left alone, and the socket probe
  confirms Kestrel does the same. `System.Uri` will not carry those three, so they are asserted
  over a raw socket only; the handler is held to the rows an `HttpClient` can send.
- The `.NET Framework` leg of the export task was built but not run: no Visual Studio here. It
  references `System.Reflection.Metadata` 8.0.0, the version the MSBuild in the .NET 8 SDK
  carries, and ships it beside the task for a host that carries another.
- Nothing here touched the response cache, rate limiting or the routing generators' binding
  rules, and nothing in the work smelled of them.

## The spec-first client rule, verbatim

A spec-first client behind the generated interface is built only if a spec-first arm files the
absence of a shared-interface client as a defect after the template and the Clients page exist;
nothing decides a code-first one.

## The next trial

The next clean-room trial's specification requires every arm to consume the service through the
generated client in its tests and to report what the client could not express: typed errors,
streaming, a mirror of the declared set. That report is the measurement the design defers
decisions to. The specification lives with the trial material rather than in this repository.
