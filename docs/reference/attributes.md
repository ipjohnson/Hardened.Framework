# Attributes

Every attribute in the framework, by the package it comes from.

## Modules and application

`Hardened.Shared.Runtime.Attributes`

| Attribute | Target | Purpose |
|---|---|---|
| `[HardenedModule]` | Class | Marks a `partial class` as a module entry point. Generates the module and a companion attribute named after it |
| `[ConfigurationModel]` | Class | Marks a `partial class` as a configuration model. Generates the interface, the properties and the registration |
| `[FromEnvironmentVariable(name)]` | Field | Populates a configuration field from an environment variable |
| `[HideConfigurationField]` | Field | Excludes a field from the generated interface |
| `[ConfigurationProvider]` | Class | Marks a configuration provider |

Every `[HardenedModule]` class also produces `<Name>Attribute`, which is how one module imports
another — `[AspNetCoreRuntime]`, `[HardenedWebModule]`, `[DynamoDbModule]` and the rest are all
generated this way. See [Modules](/guide/modules#declaring-a-module).

## Service registration

`DependencyModules.Runtime.Attributes`, from
[DependencyModules](https://ipjohnson.github.io/DependencyModules/reference/attributes)

| Attribute | Target | Purpose |
|---|---|---|
| `[SingletonService]` | Class | One instance for the application |
| `[ScopedService]` | Class | One instance per scope |
| `[TransientService]` | Class | A new instance per resolution |
| `[DependencyModule]` | Class | The DependencyModules module attribute `[HardenedModule]` builds on |
| `[Decorator]` | Class | Wraps a registered service |
| `[Decorate]` | Class | Applies a decorator to a service you do not control |
| `[Intercept]` | Class | Routes members through generated interceptors |
| `[IfEnvironment]` / `[IfNotEnvironment]` | Class | Registers only in (or outside) named environments |
| `[IfEnvironmentValue]` / `[IfNotEnvironmentValue]` | Class | Registers based on an environment variable |
| `[CrossWireService]` | Class | Cross-wires a registration between modules |

All take `As` to narrow the service type, and `Using` to choose the registration semantics.

## Requests

`Hardened.Requests.Abstract.Attributes`

| Attribute | Target | Purpose |
|---|---|---|
| `[HardenedFunction(name?)]` | Method | A function handler, addressed by name |
| `[Handler]` | Class | Marks an implementation of a [generated OpenAPI service interface](/guide/openapi) |
| `[FromBody]` | Parameter | Binds from the request body |
| `[FromServices]` | Parameter | Binds from the container |
| `[Output<T>]` | Method | Hands the response to a [view or other output](/guide/templates) instead of serialising it. Takes the response out of negotiation: unsupported `Accept` is a `406` |
| `[RawResponse(contentType?)]` | Method | [Commits](/guide/content-negotiation#forcing-a-content-type) the response to a content type and writes the value unstructured. Defaults to `text/plain` |
| `[Throws<T>(status?)]` | Method | [Declares a thrown response](/guide/responses#declaring-what-a-handler-throws) for the document. The status comes from `T`'s `[HttpStatus]`, or from the argument |

`ICustomBindingAttribute` is the interface an attribute implements to bind a parameter itself — see
[Parameter binding](/guide/parameter-binding#custom-binding).

`Hardened.Requests.Runtime.Filters`

| Attribute | Target | Purpose |
|---|---|---|
| `[Retry]` | Class, method | Re-runs the handler after a failure. `Attempts` (3), `SleepTime` (500 ms), `TotalBudget` (10 s), `AllowNonIdempotent`. Declines client errors, and non-idempotent verbs unless told otherwise |
| `[Timeout]` | Class, method, assembly | [Bounds how long the operation may take](/guide/request-timeouts). `Milliseconds` (30 s), `Status` (504), `RetryAfterSeconds`. The nearest declaration wins, and nothing is bounded until one is written |

`Hardened.Requests.Runtime.Caching`

| Attribute | Target | Purpose |
|---|---|---|
| `[CacheResponse<T>(values, …)]` | Class, method | [Stores the response](/guide/response-caching) and serves it without running the handler. `Duration`, `Scope`, `Tags`. `AllowMultiple`, and the parts compose into one key |

`Hardened.Requests.Caching.Memory`

| Attribute | Target | Purpose |
|---|---|---|
| `[HardenedMemoryResponseCache]` | Class | Registers the in-process `IResponseCacheStore`. Nothing stores a response without a store |

## Authorization

`Hardened.Requests.Runtime.Authorization`

| Attribute | Target | Purpose |
|---|---|---|
| `[AuthorizeGrants(grants)]` | Class, method | Requires every grant named. What a generator emits from a specification |
| `[AuthorizeGrants<T>]` | Class, method | Requires every grant in the [`IGrantProvider`](/guide/authorization#typed-grant-sets) `T` names. The typed spelling |
| `[Authorize<TAuth>]` | Class, method | Requires a caller established through the [authentication scheme](/guide/authentication) `TAuth`. Declares the scheme in the document |
| `[Authorize<TAuth, TPolicy>]` | Class, method | The same, and the [policy](/guide/authorization#policies)'s requirement as well. The only form that can express *or* |
| `[AllowAnonymous]` | Class, method | Makes an operation public on purpose. Beats every requirement on the same handler, including a convention |
| `[RequireAuthorization]` | Class, assembly | On the module: a handler declaring nothing is denied rather than public, and reported as `HAUTH001` at build |

**Every one of these stacks as *and*.** Attributes on a method, attributes on its controller,
attributes inherited from a base attribute and requirements added by an
[`IAuthorizationConvention`](/guide/authorization#conventions) are all conjoined into the single
`Requirement` the pipeline reads. Alternatives are expressible only inside a single policy.

`[AuthorizeGrants]` is not sealed — deriving from it is
[one of the two ways](/guide/authorization#named-attributes) to require grants without writing
strings. `IAuthorizeAttribute` is the interface anything the pipeline honours implements, including
attributes of your own, and it is what the `HAUTH001` diagnostic tests.

See [Authorization](/guide/authorization).

## Web

`Hardened.Web.Runtime.Attributes`

| Attribute | Target | Purpose |
|---|---|---|
| `[Get(path)]` | Method | A `GET` route |
| `[Post(path)]` | Method | A `POST` route |
| `[Put(path)]` | Method | A `PUT` route |
| `[Delete(path)]` | Method | A `DELETE` route |
| `[Patch(path)]` | Method | A `PATCH` route |
| `[BasePath(path)]` | Class, assembly | Prefixes every route beneath it |
| `[FromQueryString(name?)]` | Parameter | Binds from the query string |
| `[FromHeader(name?)]` | Parameter | Binds from a request header |
| `[CacheControl]` | Method | Sets cache headers. `MaxAge`, `Type`. The header half of caching; `[CacheResponse<T>]` is the store half |
| `[ServerSentEvents]` | Method | Frames an `IAsyncEnumerable<T>` as [`text/event-stream`](/guide/streaming) rather than NDJSON |
| `[WebLibrary]` | Class | Marks a web library entry point |
| `[Tag(name)]` | Class | The [OpenAPI tag](/guide/openapi-document) this controller's operations group under. Defaults to the class name minus `Controller` |
| `[Server(url, description?)]` | Class, assembly | A base URL the generated document lists under `servers` |
| `[CaseInsensitiveRoutes]` | Class | Matches this module's routes [without regard to case](/guide/routing#case-and-trailing-slashes) |
| `[RouteConstraint(name)]` | Method | Declares a [route constraint](/guide/routing#declaring-your-own-constraint). `static bool(ReadOnlySpan<char>)` |

`Hardened.Web.Runtime.Compression`

| Attribute | Target | Purpose |
|---|---|---|
| `[Compress]` | Class, method | [Compresses this operation's responses](/guide/compression) under the configured media-type rule. `Favor` picks a coding |
| `[Compress<TPredicate>(args)]` | Class, method | The same, decided by a predicate over the value the handler returned |

`Hardened.Web.Runtime.Conditional`

| Attribute | Target | Purpose |
|---|---|---|
| `[ConditionalGet]` | Class, method | Answers a caller holding the response with a [304](/guide/conditional-requests). GET handlers only |

Both features are off until the application asks for them, and both can be turned on for every
handler from the module instead:

| Attribute | Target | Purpose |
|---|---|---|
| `[Enable<HardenedCompression>]` | Class | [Compresses](/guide/compression) every response the media-type rule admits, for every client that accepts it |
| `[Enable<ConditionalGet>]` | Class | Answers a [conditional GET](/guide/conditional-requests) at every GET handler |
| `[Enable<RequestTimeouts>]` | Class | [Bounds](/guide/request-timeouts) every operation that declares no budget of its own, at 30 seconds |
| `[RequestTimeouts(ms)]` | Class | The same, with the number written. `[Enable<T>]` takes no arguments, so this is where one goes |

Each stands down for a handler carrying its own `[Compress]` or `[ConditionalGet]`, so explicit beats
convention. A budget resolves the same way, over four levels: the operation, its class, the
handler's assembly, then the entry point.

The verb attributes also declare `SuccessStatus`, the status a successful response answers with
and the document publishes; unset means 200. The `NullReturnStatus`, `ValidationErrorStatus` and
`ErrorStatus` properties they once carried are gone. See
[Returning `null`](/guide/routing#returning-null) for what decides those statuses.

## Templates

`Hardened.Templates.RazorBlade`

| Attribute | Target | Purpose |
|---|---|---|
| `[Enable<HardenedRazorTemplates>]` | Class | Generates a RazorBlade template base for a module |
| `[TemplateBase(typeof(T<>))]` | Class | On an engine's marker: the class a generated base derives from |
| `[TemplateContentType(type)]` | Class | On an engine's marker: what views on that base produce |

`[Enable<T>]` itself lives in `Hardened.Shared.Runtime.Attributes` and is the framework's one name
for every optional generated feature. It requires `new()`, and a marker that is also a
DependencyModules module has its registrations applied too. A view is named on a handler with
`[Output<T>]`.

## Console

`Hardened.Commands.Attributes`

| Attribute | Target | Purpose |
|---|---|---|
| `[Command(command)]` | Class | A command. `ParentCommand`, `Description` |
| `[Option]` | Property | Renames an option or gives it help text |
| `[FileOption]` | Property | An option whose value is a path |
| `[ExcludeOption]` | Property | A property that is not an option |

## Testing

`Hardened.Shared.Testing.Attributes`

| Attribute | Target | Purpose |
|---|---|---|
| `[HardenedTest]` | Method | Boots the application and injects the parameters. From `Hardened.Shared.Testing.xUnit` or `Hardened.Shared.Testing.NUnit` |
| `[HardenedTestEntryPoint(type)]` | Assembly, class, method | Names the application module under test |
| `[Mock]` | Parameter | Substitutes an NSubstitute mock and hands it to the test |
| `[EnvironmentName(name)]` | Assembly, class, method | The environment name for the test. Defaults to `test` |
| `[EnvironmentValue(variable, value)]` | Assembly, class, method | Sets an environment value for the test |

`Hardened.Web.Testing`

| Attribute | Target | Purpose |
|---|---|---|
| `[WebTesting]` | Assembly | Installs `ITestWebApp`, the test credential source, and a typed client for every test parameter that names one |
| `[Grants(params string[])]` | Parameter, method, class, assembly | The grants a request is sent with, as `X-Test-Grants`. The narrowest wins |
| `[Subject(name)]` | Parameter, method, class, assembly | Which caller, as `X-Test-Subject` |
| `[Anonymous]` | Parameter, method, class, assembly | No credential, cancelling whatever a wider level declared |
| `[PipelineHost]` | Assembly, class, method | Runs the test on the in-process pipeline where a wider level named a [host](/guide/testing-hosts) |

`Hardened.Web.Kestrel.Testing` and `Hardened.Web.AspNetCore.Testing`

| Attribute | Target | Purpose |
|---|---|---|
| `[KestrelTesting]` | Assembly | Runs a test carrying `[KestrelRuntime]` on Kestrel, on a loopback port. See [Test hosts](/guide/testing-hosts) |
| `[AspNetCoreTesting(composition?)]` | Assembly | Runs a test carrying `[AspNetCoreRuntime]` inside a real `WebApplication`. The composition names an `IAspNetCoreTestComposition` that arranges the middleware |

`Hardened.Kiota.Testing` and `Hardened.Refit.Testing`

| Attribute | Target | Purpose |
|---|---|---|
| `[KiotaTesting]` | Assembly | Makes every Kiota client a test parameter built over the pipeline. See [Typed clients](/guide/testing-clients) |
| `[RefitTesting]` | Assembly | The same for a Refit interface |

## AWS

From [Hardened.Amz](https://github.com/ipjohnson/Hardened.Amz).

| Attribute | Namespace | Purpose |
|---|---|---|
| `[LambdaFunctionModule]` | `Hardened.Amz.Function.Lambda.Runtime.DependencyInjection` | Lambda invocation runtime and the request pipeline. Required by every function application |
| `[LambdaWebModule]` | `Hardened.Amz.Web.Lambda.Runtime.DependencyInjection` | API Gateway runtime and the web pipeline. Required by every Lambda web application |
| `[LambdaWebApplication(Version)]` | `Hardened.Amz.Web.Lambda.Runtime` | States the payload format. `ProxyIntegrationType.HttpApiV2` is the default and the only implemented value; `.ApiGateway` is `HRDAWS001` |
| `[SqsLambda]` | `Hardened.Amz.Function.Sqs.Runtime` | SQS batch runtime. Applied alongside `[LambdaFunctionModule]` |
| `[DynamoStreamLambda]` | `Hardened.Amz.Function.DDB.Runtime` | DynamoDB Streams runtime. Applied alongside `[LambdaFunctionModule]` |
| `[DynamoDbModule]` | `Hardened.Amz.DynamoDbClient` | Registers `IDynamoDbClientProvider` |
| `[HardenedCdk]` | `Hardened.Amz.Cdk` | CDK deployment application |
| `[NewImage]` / `[OldImage]` | `Hardened.Amz.Function.DDB.Runtime.Attributes` | Binds a stream record's images |
| `[FromContext(name?)]` | `Hardened.Amz.Function.Lambda.Runtime` | Binds a named value from the invocation's headers |
| `[ThrowException]` | `Hardened.Amz.Function.Lambda.Runtime` | Rethrows, so the invocation fails instead of returning the error |
| `[LambdaFunctionTesting]` | `Hardened.Amz.Function.Lambda.Testing` | Installs the Lambda test harnesses |
| `[LocalDynamoDb(Image?)]` | `Hardened.Amz.DynamoDbClient.Testing` | Points the client provider at DynamoDB Local in a container |

Whether a Lambda response streams is `HARDENED_LAMBDA_RESPONSE_MODE`, a deployment setting rather
than an attribute; see [Response mode](/aws/lambda-web#response-mode).
