# Attributes

This page lists every public attribute class in the Hardened packages, grouped by what each one is for. It also lists the attributes of DependencyModules and ValidationModules that the Hardened guides cover. Three Hardened packages depend on those libraries: `Hardened.Shared.Runtime` on `DependencyModules.Runtime`, `Hardened.Shared.Testing` on `DependencyModules.Testing`, and `Hardened.Requests.Runtime` on `ValidationModules.Runtime`.

The Targets column gives the attribute's `AttributeUsage`, its own or its base class's. It says Any where neither declares one. The compiler then accepts the attribute on any declaration. Such a row's description says where the build reads it. Repeatable means `AllowMultiple = true`.

A module attribute is the attribute that the build writes for a module class. The attribute class is named after the module class with `Attribute` appended: the module class `KestrelRuntime` gets `[KestrelRuntime]`. Applying the attribute imports the module. A module attribute's properties are the module class's public settable properties. Every module attribute accepts a class, an assembly, a method and a parameter, and is repeatable. The tables show these targets as Module attribute.

## Modules and configuration

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[HardenedModule]` | Any | `Hardened.Shared.Runtime.Attributes` in `Hardened.Shared.Runtime` | Makes the `partial` class it is on a module. The build writes the module's attribute | [Modules](/guide/modules) |
| `[<Module>]`, such as `[TodosLibrary]` | Class, assembly, method, parameter; repeatable | The module's namespace, in the module's project | Imports the module. The build writes it for each `[HardenedModule]` class<br>Properties: one named property for each public settable property of the module | [Modules](/guide/modules) |
| `[Enable<TFeature>]`<br>`TFeature` has a public parameterless constructor | Class, assembly; repeatable | `Hardened.Shared.Runtime.Attributes` in `Hardened.Shared.Runtime` | Turns on the feature `TFeature` for the module class it is on | This page, [Features](#features) |
| `[HardenedCoreModule]` | Module attribute | `Hardened.Shared.Runtime.DependencyInjection` in `Hardened.Shared.Runtime` | Registers shared services, such as the JSON serializer configuration. `[HardenedRequestModule]` imports it | [Modules](/guide/modules) |
| `[HardenedRequestModule]` | Module attribute | `Hardened.Requests.Runtime.DependencyInjection` in `Hardened.Requests.Runtime` | Registers the request pipeline. `[HardenedWebModule]`, `[LambdaRuntimeModule]` and `[FunctionsRuntimeModule]` import it | [Modules](/guide/modules) |
| `[ConfigurationModel]` | Any | `Hardened.Shared.Runtime.Attributes` in `Hardened.Shared.Runtime` | Makes the `partial` class it is on a configuration model. The build writes an interface named `I` plus the class name, with a property for each field, and registers `IOptions<>` of the interface | [Configuration](/guide/configuration) |
| `[FromEnvironmentVariable(environmentVariable)]`<br>`string environmentVariable` | Any | `Hardened.Shared.Runtime.Attributes` in `Hardened.Shared.Runtime` | On a field of a configuration model: fills the field from that environment variable | [Configuration](/guide/configuration) |
| `[HideConfigurationField]` | Any | `Hardened.Shared.Runtime.Attributes` in `Hardened.Shared.Runtime` | On a field of a configuration model: the build writes no property for the field | [Configuration](/guide/configuration) |
| `[ConfigurationProvider]` | Any | `Hardened.Shared.Runtime.Attributes` in `Hardened.Shared.Runtime` | Nothing in the build or at run time reads it | None |

The build reads `[FromEnvironmentVariable]` and `[HideConfigurationField]` on a field of the model class, and not on a property. `[HardenedCoreModule]` and `[HardenedRequestModule]` come with every host's module attribute. An application does not write them.

## Services

The service attributes come from DependencyModules. Each one is in the namespace `DependencyModules.Runtime.Attributes`, in the package `DependencyModules.Runtime` 1.7.0. A project that references `Hardened.Shared.Runtime` needs no reference of its own to the package. [Registering services](/guide/services) covers each attribute.

| Attribute | Targets | What it does |
|---|---|---|
| `[SingletonService]` | Class, method; repeatable | Registers the class, one instance for the application<br>Properties: `As` (unset: the service type the build picks), `Using` (`RegistrationType.Add`), `Key` (none), `Realm` (none), `Order` (0) |
| `[ScopedService]` | Class, method; repeatable | Registers the class, one instance per request<br>Properties: the same as `[SingletonService]` |
| `[TransientService]` | Class, method; repeatable | Registers the class, a new instance each time it is resolved<br>Properties: the same as `[SingletonService]` |
| `[CrossWireService]` | Class, method; repeatable | Registers the class as itself and as every interface it declares, all resolving to one instance<br>Properties: `Lifetime` (`ServiceLifetime.Singleton`), `Using` (`RegistrationType.Add`), `Key` (none), `Realm` (none) |
| `[IfEnvironment(params environmentNames)]`<br>`string[] environmentNames` | Class | Registers the class only in the named environments |
| `[IfNotEnvironment(params environmentNames)]`<br>`string[] environmentNames` | Class | Registers the class only outside the named environments |
| `[IfEnvironmentValue(key)]`, `[IfEnvironmentValue(key, value)]`<br>`string key`, `string value` | Class; repeatable | Registers the class when the variable has a value, or has exactly that value |
| `[IfNotEnvironmentValue(key)]`, `[IfNotEnvironmentValue(key, value)]`<br>`string key`, `string value` | Class; repeatable | Registers the class when the variable has no value, or not exactly that value |
| `[Decorator]` | Class | Makes the class wrap every registration of the service it implements<br>Properties: `Service` (none), `Order` (0), `Realm` (none), `Implementation` (none) |
| `[Decorate(service, decorator)]`<br>`Type service`, `Type decorator` | Class; repeatable | Applies a decorator to a service from an assembly you do not control<br>Property: `Order` (0) |
| `[Intercept(params interceptors)]`<br>`Type[] interceptors` | Class; repeatable | Does nothing in a Hardened project: the interceptor never runs, and the build reports nothing<br>Properties: `Service` (none), `Order` (0), `Realm` (none), `Lifetime` (`ServiceLifetime.Singleton`), `Members` (`InterceptedMembers.All`) |

## Handlers and binding

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[Get(path)]`<br>`string path = ""`, which routes `/` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Makes the method a handler for GET and HEAD at the path template<br>Property: `SuccessStatus` (unset: 200) | [Routing](/guide/routing) |
| `[Post(path)]`<br>As `[Get]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Makes the method a handler for POST at the path template | [Routing](/guide/routing) |
| `[Put(path)]`<br>As `[Get]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Makes the method a handler for PUT at the path template | [Routing](/guide/routing) |
| `[Patch(path)]`<br>As `[Get]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Makes the method a handler for PATCH at the path template | [Routing](/guide/routing) |
| `[Delete(path)]`<br>As `[Get]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Makes the method a handler for DELETE at the path template | [Routing](/guide/routing) |
| `[BasePath(path)]`<br>`string path` | Class, assembly | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | On the module that holds the routes: prefixes every route in the module's project | [Routing](/guide/routing) |
| `[CaseInsensitiveRoutes]` | Class | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | On the module that holds the routes: matches its routes without regard to case | [Routing](/guide/routing) |
| `[RouteConstraint(name)]`<br>`string name` | Method; repeatable | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | On a `static bool` method that takes a `ReadOnlySpan<char>`: declares the route constraint `name` | [Routing](/guide/routing) |
| `[FromQueryString(name)]`<br>`string? name = null`, which reads the parameter's own name | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Binds the parameter from the query string value of that name | [Parameter binding](/guide/parameter-binding) |
| `[FromHeader(name)]`<br>As `[FromQueryString]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Binds the parameter from the request header of that name | [Parameter binding](/guide/parameter-binding) |
| `[FromCookie(name)]`<br>As `[FromQueryString]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Binds the parameter from the cookie of that name | [Parameter binding](/guide/parameter-binding) |
| `[FromForm(name)]`<br>As `[FromQueryString]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Binds the parameter from a field or a file of an `application/x-www-form-urlencoded` or `multipart/form-data` body | [Forms and files](/guide/forms) |
| `[FromBody]` | Any | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Binds the parameter from the request body | [Parameter binding](/guide/parameter-binding) |
| `[FromServices]` | Any | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Resolves the parameter from the container | [Registering services](/guide/services) |
| `[Handler]` | Class | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Marks a class that implements a service interface generated from a contract. The class answers the contract's operations | [Generating from OpenAPI](/guide/openapi) |
| `[ResponseOnly]` | Property, parameter | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Marks a member that responses carry and clients do not send. A contract's `readOnly` generates it. A request body can leave the member out even when it is required | [Generating from OpenAPI](/guide/openapi) |
| `[RequestOnly]` | Property, parameter | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Marks a member that clients send and responses do not carry. A contract's `writeOnly` generates it. Nothing enforces it | [Generating from OpenAPI](/guide/openapi) |

The build reads a route attribute on a method. It reads a binding attribute on a parameter. Neither `[ResponseOnly]` nor `[RequestOnly]` is enforced. A request can set a `readOnly` member. A response writes a `writeOnly` member.

## Responses and serialization

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[HttpStatus(statusCode)]`<br>`int statusCode` | Class, struct | `Hardened.Web.Runtime.Responses` in `Hardened.Web.Runtime` | On a response type: the status a response of that type answers with | [Declared responses](/guide/responses) |
| `[Throws<TError>]`, `[Throws<TError>(statusCode)]`<br>`int statusCode` (unset: the status in `TError`'s `[HttpStatus]`) | Method; repeatable | `Hardened.Web.Runtime.Responses` in `Hardened.Web.Runtime` | Declares a non-2xx response that the handler answers by throwing, so the OpenAPI document lists it<br>Property: `Description` (none) | [Declared responses](/guide/responses) |
| `[AnswersHeader(status, name)]`<br>`int status`, `string name` | Class, method, interface; repeatable | `Hardened.Requests.Abstract.Responses` in `Hardened.Requests.Abstract` | Lists a header on the response with that status in the OpenAPI document<br>Properties: `Description` (none), `Methods` (none), `NotWhenStreaming` (`false`) | [Declared responses](/guide/responses) |
| `[ResponseModel(model)]`<br>`ResponseModel model`: `Throws`, `Response` or `Union` | Class | `Hardened.Requests.Abstract.Responses` in `Hardened.Requests.Abstract` | Names the response model of the module's handlers. A code-first build generates the same code with it or without it | [Declared responses](/guide/responses) |
| `[Produces(params contentTypes)]`<br>`string[] contentTypes` | Class, method, assembly | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | The media types the operation answers with, in preference order | [Content negotiation](/guide/content-negotiation) |
| `[RawResponse(contentType)]`<br>`string contentType = "text/plain"` | Method | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Obsolete. `[Produces]` under an older name, and the compiler warns with `CS0618` naming `[Produces]` | [Content negotiation](/guide/content-negotiation) |
| `[ContentNegotiation(mode)]`<br>`ContentNegotiationMode mode`: `Strict` answers 406, `Lenient` answers with the default serializer | Class, assembly | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Chooses the answer to a request for a media type that no operation produces. See [Limits](#limits) | [Content negotiation](/guide/content-negotiation) |
| `[WritesContentType(params contentTypes)]`<br>`string[] contentTypes` | Assembly; repeatable | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Names the media types a serializer in the assembly writes a model as. The build reads it before it warns `HRDR012` | [Content negotiation](/guide/content-negotiation) |
| `[JsonErrorBodies]` | Class, assembly | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | On the module that declares the routes: answers every response with a status of 400 or more as JSON, whatever the request negotiated | [Content negotiation](/guide/content-negotiation) |
| `[JsonEnumNaming(naming)]`<br>`EnumNaming naming`: `MemberName`, `CamelCase`, `KebabCaseLower`, `SnakeCaseLower` or `SnakeCaseUpper` | Enum, assembly | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | How the values of one enum, or of every enum in the assembly, are written in JSON. Without it they are camelCase | [JSON serialization](/guide/json) |
| `[AotSerializerModule]` | Class | `Hardened.Requests.Runtime` in `Hardened.Requests.Runtime` | On the application module: registers JSON serializers that read only from the registered resolvers, for Native AOT | [JSON serialization](/guide/json) |
| `[MessagePackSerializerLibrary]` | Module attribute | `Hardened.Requests.Serializers.MessagePack` in `Hardened.Requests.Serializers.MessagePack` | Registers the MessagePack reader and writer | [MessagePack](/guide/message-pack) |
| `[ServerSentEvents]` | Method | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Sends the handler's `IAsyncEnumerable<T>` as `text/event-stream` in place of newline-delimited JSON | [Streaming responses](/guide/streaming) |

`[RawResponse]` carries `[Obsolete]` with the message `Use [Produces] instead. [RawResponse("text/csv")] is [Produces("text/csv")].`

The enums that the constructors take are in these namespaces:

| Type | Namespace |
|---|---|
| `ResponseModel` | `Hardened.Requests.Abstract.Responses` |
| `ContentNegotiationMode` | `Hardened.Requests.Abstract.Serializer` |
| `EnumNaming` | `Hardened.Requests.Abstract.Attributes` |

## Validation

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[Validate<TValidated>]`<br>`TValidated` is a reference type | Class, method | `Hardened.Requests.Runtime.Validation` in `Hardened.Requests.Runtime` | The build attaches it to a handler whose parameters declare constraints. It runs every registered `IValidatorFor<TValidated>` against the bound parameters | None |
| `[ValidationMode(stopMode)]`<br>`ValidationStopMode stopMode`: `CollectAll` or `StopOnFirstError` | Class, method | `Hardened.Requests.Runtime.Validation` in `Hardened.Requests.Runtime` | Whether a request that fails validation is answered with every failure or with the first. A handler under no declaration gets every failure. The nearest declaration wins. `ValidationStopMode` is in the namespace `ValidationModules`. See [Limits](#limits) | [Validation](/guide/validation) |

`TValidated` is an interface that the generated parameters class implements. With no `IValidatorFor<TValidated>` registered, the request fails with an `InvalidOperationException` that names the type.

The constraint attributes come from ValidationModules. Each one is in the namespace `ValidationModules.Constraints`, in the package `ValidationModules.Runtime` 1.1.0. A project that references `Hardened.Requests.Runtime` needs no reference of its own to the package. [Validation](/guide/validation) covers each constraint. Every constraint also takes the properties `Code`, `Message`, `When` and `Unless`, all unset.

| Attribute | Targets | What it does |
|---|---|---|
| `[Required]` | Property, field, parameter; repeatable | The value is present<br>Property: `AllowEmptyStrings` (`false`) |
| `[StringLength(min, max)]`<br>`int min = 0`, `int max = int.MaxValue`, or `Min` and `Max` | Property, field, parameter; repeatable | A string's length is within the bounds |
| `[Range(min, max)]`<br>`min` and `max` as `int`, `long`, `double` or `string`, or `Min` and `Max` | Property, field, parameter; repeatable | The value is within the bounds<br>Properties: `ExclusiveMin` (`false`), `ExclusiveMax` (`false`) |
| `[Pattern(pattern)]`, `[Pattern(regexProvider, regexMember)]`<br>`string pattern`, or `Type regexProvider` and `string regexMember` for a `[GeneratedRegex]` method | Property, field, parameter; repeatable | The string matches the regular expression<br>Properties: `Options` (`RegexOptions.None`), `MatchTimeoutMilliseconds` (0) |
| `[ItemCount(min, max)]`<br>As `[StringLength]` | Property, field, parameter; repeatable | A collection's count is within the bounds |
| `[AllowedValues(params values)]`<br>`object[] values` | Property, field, parameter; repeatable | The value is one of these<br>Property: `Comparison` (`StringComparison.Ordinal`) |
| `[DeniedValues(params values)]`<br>`object[] values` | Property, field, parameter; repeatable | The value is none of these |
| `[MultipleOf(divisor)]`<br>`divisor` as `int`, `long`, `double` or `string` | Property, field, parameter; repeatable | The number is a multiple of the divisor |
| `[UniqueItems]` | Property, field, parameter; repeatable | A collection's items are distinct |
| `[EmailAddress]` | Property, field, parameter; repeatable | The string is an email address |
| `[Phone]` | Property, field, parameter; repeatable | The string is a phone number |
| `[Url]` | Property, field, parameter; repeatable | The string is a URL |
| `[CreditCard]` | Property, field, parameter; repeatable | The string is a credit card number |
| `[Base64String]` | Property, field, parameter; repeatable | The string is Base64 |
| `[FileExtensions]` | Property, field, parameter; repeatable | The string ends in one of the extensions<br>Property: `Extensions` (`png,jpg,jpeg,gif`) |
| `[ValidateNested]`, `[ValidateNested(polymorphism)]`<br>`Polymorphism polymorphism` | Property, field, parameter; repeatable | Checks the constraints of the member's own type |

## Filters

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[Retry]` | Class, method | `Hardened.Requests.Runtime.Filters` in `Hardened.Requests.Runtime` | Runs the handler again when an attempt fails<br>Properties: `Attempts` (3), `Retries` (reads and sets `Attempts`), `SleepTime` (500 ms), `TotalBudget` (10000 ms), `AllowNonIdempotent` (`false`) | [The execution pipeline](/guide/execution-pipeline) |
| `[Timeout]` | Class, method, assembly | `Hardened.Requests.Runtime.Filters` in `Hardened.Requests.Runtime` | Bounds how long the operation may take. The nearest declaration wins<br>Properties: `Milliseconds` (30000), `Status` (504), `RetryAfterSeconds` (0, which writes no `Retry-After`), `Deadline` (`true`) | [Request timeouts](/guide/request-timeouts) |
| `[RequestTimeouts(milliseconds)]`<br>`int milliseconds` | Module attribute | `Hardened.Requests.Runtime.Filters` in `Hardened.Requests.Runtime` | Bounds every handler in the application that no nearer declaration bounds | [Request timeouts](/guide/request-timeouts) |
| `[RateLimit]` | Class, method; repeatable | `Hardened.Requests.Runtime.RateLimiting` in `Hardened.Requests.Runtime` | Limits how many requests each caller can make in a window. A request past the limit gets 429<br>Properties: `PermitLimit` (100), `WindowSeconds` (60), `Name` (`"default"`), `Scope` (`RateLimitScope.Transport`) | [Rate limiting](/guide/rate-limiting) |
| `[CacheResponse<TProvider>(params values)]`<br>`string[] values`<br>`TProvider` implements `ICacheKeyProvider` | Class, method; repeatable | `Hardened.Requests.Runtime.Caching` in `Hardened.Requests.Runtime` | Stores the handler's response and serves it to a later request with the same key, without running the handler. `TProvider` builds the key from the values<br>Properties: `Duration` (0, which stores for 60 seconds), `Scope` (`CacheScope.Unstated`), `Tags` (empty) | [Response caching](/guide/response-caching) |
| `[CacheControl]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Writes the `Cache-Control` header on the handler's responses, or on those of every handler in the class. It stores nothing<br>Properties: `MaxAge` (0), `Type` (`CacheControlEnum.MaxAge \| CacheControlEnum.Public`) | [Response caching](/guide/response-caching) |
| `[HardenedMemoryResponseCache]` | Module attribute | `Hardened.Requests.Caching.Memory` in `Hardened.Requests.Caching.Memory` | Registers the in-process store that `[CacheResponse<TProvider>]` keeps responses in | [Response caching](/guide/response-caching) |
| `[Compress]` | Class, method | `Hardened.Web.Runtime.Compression` in `Hardened.Web.Runtime` | Compresses the handler's response for a request whose `Accept-Encoding` names gzip or Brotli<br>Property: `Favor` (`CompressionType.Default`, which follows the configured order) | [Compression](/guide/compression) |
| `[Compress<TPredicate>(params args)]`<br>`object[] args`<br>`TPredicate` implements `ICompressionPredicate` | Class, method | `Hardened.Web.Runtime.Compression` in `Hardened.Web.Runtime` | As `[Compress]`, when `TPredicate` says so, given the value the handler returned<br>Property: `Favor` (as `[Compress]`) | [Compression](/guide/compression) |
| `[ResponseCompression]` | Module attribute | `Hardened.Web.Runtime.Compression` in `Hardened.Web.Runtime` | Compresses every response in the application. It applies the module that `[Enable<ResponseCompression>]` applies | [Compression](/guide/compression) |
| `[ConditionalGet]` | Class, method | `Hardened.Web.Runtime.Conditional` in `Hardened.Web.Runtime` | Gives a GET handler's response an `ETag`, and answers a request whose `If-None-Match` matches with 304 and no body | [Conditional requests](/guide/conditional-requests) |
| `[Cors]` | Class, method | `Hardened.Web.Runtime.Cors` in `Hardened.Web.Runtime` | Limits CORS to the handlers it covers, which answer with the application's `CorsConfiguration`. Once a handler or a module declares it, a route without a declaration gets no CORS headers | [CORS](/guide/cors) |
| `[Cors<TPolicy>]` | Class, method | `Hardened.Web.Runtime.Cors` in `Hardened.Web.Runtime` | As `[Cors]`, with the configuration that `AddCorsPolicy<TPolicy>` registered. `TPolicy` can be any type | [CORS](/guide/cors) |
| `[AnswersStatus(status, body)]`, `[AnswersStatus(status)]`<br>`int status`, `Type body` | Class, interface; repeatable | `Hardened.Requests.Abstract.Responses` in `Hardened.Requests.Abstract` | On a filter attribute: every operation that carries the filter publishes the status, with that body, in the OpenAPI document<br>Properties: `Description`, `Methods` and `StatusFrom` (none), `NotWhenStreaming` (`false`) | [The execution pipeline](/guide/execution-pipeline) |
| `[ReadsHeader(name)]`<br>`string name` | Class, method, interface; repeatable | `Hardened.Web.Runtime.Responses` in `Hardened.Web.Runtime` | On a filter attribute: every operation that carries the filter publishes the header as an optional parameter<br>Properties: `Description` and `Methods` (none), `NotWhenStreaming` (`false`) | [The execution pipeline](/guide/execution-pipeline) |

The defaults in the table are the values of a new instance. A filter attribute that goes on a method or a class also goes on a `[HardenedModule]` class. There it covers the handlers compiled with the module. [Limits](#limits) lists the enum values that fail the build on a module class.

The enums in the table are in these namespaces:

| Type | Namespace | Values |
|---|---|---|
| `RateLimitScope` | `Hardened.Requests.Runtime.RateLimiting` | `Transport`, `Principal` |
| `CacheScope` | `Hardened.Requests.Abstract.Caching` | `Unstated`, `AllCallers`, `PerCaller` |
| `CompressionType` | `Hardened.Web.Runtime.Compression` | `Default`, `GZip`, `Br` |
| `CacheControlEnum` | `Hardened.Web.Runtime.CacheControl` | Flags: `MaxAge`, `NoCache`, `NoStore`, `NoTransform`, `Public`, `Private` |

## Authentication and authorization

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[HttpAuthenticationScheme(scheme)]`<br>`string scheme` | Class | `Hardened.Requests.Abstract.Authorization` in `Hardened.Requests.Abstract` | On an `IAuthenticationScheme` class: publishes it as an HTTP scheme, such as `bearer`<br>Properties: `BearerFormat` (none), `Description` (none) | [Authentication](/guide/authentication) |
| `[ApiKeyAuthenticationScheme(name, location)]`<br>`string name`, `ApiKeyLocation location` | Class | `Hardened.Requests.Abstract.Authorization` in `Hardened.Requests.Abstract` | On an `IAuthenticationScheme` class: publishes it as an API key in a header, a query parameter or a cookie<br>Property: `Description` (none) | [Authentication](/guide/authentication) |
| `[OAuth2AuthenticationScheme(flow)]`<br>`OAuth2Flow flow` | Class | `Hardened.Requests.Abstract.Authorization` in `Hardened.Requests.Abstract` | On an `IAuthenticationScheme` class: publishes it as OAuth2 with one flow<br>Properties: `AuthorizationUrl`, `TokenUrl`, `RefreshUrl` and `Description` (none) | [Authentication](/guide/authentication) |
| `[Authorize<TAuth>]`<br>`TAuth` implements `IAuthenticationScheme` | Class, method | `Hardened.Requests.Runtime.Authorization` in `Hardened.Requests.Runtime` | Requires an authenticated caller, and names the scheme `TAuth` in the OpenAPI document | [Authentication](/guide/authentication) |
| `[Authorize<TAuth, TPolicy>]`<br>`TPolicy` implements `IAuthorizationPolicy` and has a public parameterless constructor | Class, method | `Hardened.Requests.Runtime.Authorization` in `Hardened.Requests.Runtime` | Requires an authenticated caller who satisfies the policy `TPolicy` | [Authorization](/guide/authorization) |
| `[AuthorizeGrants(params grants)]`<br>`string[] grants` | Class, method; repeatable | `Hardened.Requests.Runtime.Authorization` in `Hardened.Requests.Runtime` | Requires every grant named | [Authorization](/guide/authorization) |
| `[AuthorizeGrants<T>]`<br>`T` implements `IGrantProvider` and has a public parameterless constructor | Class, method; repeatable | `Hardened.Requests.Runtime.Authorization` in `Hardened.Requests.Runtime` | Requires every grant that `T` names | [Authorization](/guide/authorization) |
| `[AllowAnonymous]` | Class, method | `Hardened.Requests.Runtime.Authorization` in `Hardened.Requests.Runtime` | Makes the handler public. It overrides every requirement | [Authorization](/guide/authorization) |
| `[RequireAuthorization]` | Class, assembly | `Hardened.Requests.Runtime.Authorization` in `Hardened.Requests.Runtime` | On a `[HardenedModule]` class: every handler in the application that declares no authorization requires an authenticated caller | [Authorization](/guide/authorization) |

The types that the table names are in these namespaces:

| Type | Namespace | Values |
|---|---|---|
| `ApiKeyLocation` | `Hardened.Requests.Abstract.Authorization` | `Header`, `Query`, `Cookie` |
| `OAuth2Flow` | `Hardened.Requests.Abstract.Authorization` | `AuthorizationCode`, `ClientCredentials`, `Implicit`, `Password` |
| `IAuthenticationScheme` | `Hardened.Requests.Abstract.Authorization` | |
| `IAuthorizationPolicy` | `Hardened.Requests.Abstract.Authorization` | |
| `IGrantProvider` | `Hardened.Requests.Runtime.Authorization` | |

[Authorization](/guide/authorization) covers how the requirements on a method, its class and a module combine.

## Web hosts and the OpenAPI document

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[HardenedWebModule]` | Module attribute | `Hardened.Web.Runtime.DependencyInjection` in `Hardened.Web.Runtime` | Serves the routes, and `/health/live` and `/health/ready`. `[KestrelRuntime]`, `[AspNetCoreRuntime]` and `[CloudRunRuntime]` import it | [Hosts](/guide/hosts) |
| `[KestrelRuntime]` | Module attribute | `Hardened.Web.Kestrel.Runtime` in `Hardened.Web.Kestrel.Runtime` | Runs the application on Kestrel, without the ASP.NET Core request pipeline | [Hosts](/guide/hosts) |
| `[AspNetCoreRuntime]` | Module attribute | `Hardened.Web.AspNetCore.Runtime` in `Hardened.Web.AspNetCore.Runtime` | Runs the application inside the ASP.NET Core request pipeline, where `app.UseHardened()` adds it | [Hosts](/guide/hosts) |
| `[OpenApiInfo(title, version, description)]`<br>`string title`, `string version = "1.0.0"`, `string? description = null` | Class | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | On the module that publishes the document: the document's `info` | [The OpenAPI document](/guide/openapi-document) |
| `[Server(url, description)]`<br>`string url`, `string? description = null` | Class, assembly; repeatable | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | On the module that publishes the document: adds the URL to the document's `servers` | [The OpenAPI document](/guide/openapi-document) |
| `[Tag(name)]`<br>`string name` | Class | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | The tag the class's operations are published under. Without it, the class name without a `Controller` suffix | [The OpenAPI document](/guide/openapi-document) |
| `[Operation(id)]`<br>`string id` | Method | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | The `operationId` the handler publishes. Without it, the method name with its first letter lower-cased | [The OpenAPI document](/guide/openapi-document) |
| `[OpenApiDocumentPath(path)]`<br>`string path` | Class | `Hardened.Web.Runtime.OpenApi` in `Hardened.Web.Runtime` | On a feature marker class: `[Enable<>]` of the class serves the document at the path in place of `/openapi.json` | [The OpenAPI document](/guide/openapi-document) |
| `[HardenedOpenApiUi]` | Module attribute | `Hardened.Web.Runtime.OpenApi` in `Hardened.Web.Runtime` | Serves a reference page for the OpenAPI document<br>Properties: `Path` (`/docs`), `Title` (`API Reference`), `DocumentPath` (`/openapi.json`), `ScriptUrl` (`@scalar/api-reference` 1.65.1 from jsDelivr), `ScriptIntegrity` (that script's SHA-384 hash), `DecodeMessagePack` (`false`), `MessagePackScriptUrl` (`@msgpack/msgpack` 3.1.3 from jsDelivr), `Environments` (unset, which serves the page in every environment) | [The OpenAPI document](/guide/openapi-document) |
| `[HardenedStaticContent]` | Module attribute | `Hardened.Web.StaticContent` in `Hardened.Web.StaticContent` | Serves a directory of files<br>Properties: `Path` (`wwwroot`), `FallBackFile` (none) | None |
| `[WebLibrary]` | Any | `Hardened.Web.Runtime.Attributes` in `Hardened.Web.Runtime` | Nothing in the build or at run time reads it | None |

`[LambdaHttpModule]`, `[CloudRunRuntime]` and `[HttpModule]` are the web hosts of the three clouds. Their rows are under [AWS](#aws), [Google Cloud](#google-cloud) and [Azure](#azure).

The string properties of `[HardenedOpenApiUi]` keep the module's defaults when the attribute leaves them unset. The full `ScriptUrl` default is `https://cdn.jsdelivr.net/npm/@scalar/api-reference@1.65.1/dist/browser/standalone.js`. The full `MessagePackScriptUrl` default is `https://cdn.jsdelivr.net/npm/@msgpack/msgpack@3.1.3/dist.umd/msgpack.min.js`. `Environments` is a comma-separated list of environment names.

The build's `HRDR004` message says to move shared handlers `into a [WebLibrary] project`.

## Views

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[Output<TOutput>]`<br>`TOutput` implements `IHardenedResponseOutput` and has a public parameterless constructor | Method | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Renders the handler's return value with the view `TOutput`, in place of serializing it | [Views](/guide/views) |
| `[TemplateBase(baseType)]`<br>`Type baseType` | Class | `Hardened.Requests.Abstract.Templates` in `Hardened.Requests.Abstract` | On a template engine's feature marker: the class that the generated view base derives from | None |
| `[TemplateContentType(contentType)]`<br>`string contentType` | Class | `Hardened.Requests.Abstract.Templates` in `Hardened.Requests.Abstract` | On a template engine's feature marker: the media type that views on that base produce | None |

`RazorTemplates` is the marker that `[Enable<RazorTemplates>]` takes. It carries `[TemplateBase(typeof(HardenedHtmlTemplate<>))]` and `[TemplateContentType("text/html; charset=utf-8")]`. `IHardenedResponseOutput` is in `Hardened.Requests.Abstract.Outputs`.

## Features

The table lists each type that `[Enable<TFeature>]` takes as `TFeature`.

| `TFeature` | Namespace and package | Turns on | Page |
|---|---|---|---|
| `OpenApiDocumentPublishing` | `Hardened.Web.Runtime.OpenApi` in `Hardened.Web.Runtime` | Embeds the OpenAPI document written from the module's routes, and serves it at `/openapi.json` | [The OpenAPI document](/guide/openapi-document) |
| A class of your own with `[OpenApiDocumentPath(path)]` | Your own | The same, served at `path` | [The OpenAPI document](/guide/openapi-document) |
| `ConditionalGet` | `Hardened.Web.Runtime.Conditional` in `Hardened.Web.Runtime` | `[ConditionalGet]` on every GET handler in the application | [Conditional requests](/guide/conditional-requests) |
| `ResponseCompression` | `Hardened.Web.Runtime.Compression` in `Hardened.Web.Runtime` | Compression of every response in the application | [Compression](/guide/compression) |
| `RequestTimeouts` | `Hardened.Requests.Runtime.Filters` in `Hardened.Requests.Runtime` | A 30-second budget for every handler that no nearer declaration bounds | [Request timeouts](/guide/request-timeouts) |
| `RazorTemplates` | `Hardened.Templates.RazorBlade` in `Hardened.Templates.RazorBlade` | The generated base class that the module's RazorBlade views inherit | [Views](/guide/views) |

The build reads `[Enable<TFeature>]` on the module class. `[assembly: Enable<TFeature>]` compiles and turns nothing on. The build also applies the registrations of a `TFeature` that is a module.

## Testing

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[ModuleTest]` | Method | `DependencyModules.xUnit.Attributes` in `DependencyModules.xUnit`, or in `DependencyModules.xUnit4` for `xunit.v3` 4.x | Builds the application for the test method and supplies its parameters, under xUnit v3<br>xUnit's `FactAttribute` properties: `DisplayName`, `Skip`, `SkipExceptions`, `SkipType`, `SkipUnless`, `SkipWhen` (none), `Explicit` (`false`), `Timeout` (0) | [Writing a test](/guide/testing) |
| `[ModuleTest]` | Method | `DependencyModules.NUnit.Attributes` in `DependencyModules.NUnit` | The same, under NUnit | [Writing a test](/guide/testing) |
| `[HardenedTestEntryPoint(entryPoint)]`<br>`Type entryPoint` | Class, method, assembly | `Hardened.Shared.Testing.Attributes` in `Hardened.Shared.Testing` | Names the module each test builds | [Writing a test](/guide/testing) |
| `[EnvironmentName(name)]`<br>`string name` | Any | `Hardened.Shared.Testing.Attributes` in `Hardened.Shared.Testing` | On a test method, its class or the assembly: the name of the test's environment. Without it, `test` | [Writing a test](/guide/testing) |
| `[EnvironmentValue(variable, value)]`<br>`string variable`, `string value` | Any | `Hardened.Shared.Testing.Attributes` in `Hardened.Shared.Testing` | On a test method, its class or the assembly: a variable in the test's environment. It is not repeatable | [Writing a test](/guide/testing) |
| `[WebTesting]` | Assembly | `Hardened.Web.Testing` in `Hardened.Web.Testing` | Registers `ITestWebApp`, the test credential source, and a client for each test parameter of a client type | [Sending requests](/guide/testing-web) |
| `[Grants(params grants)]`<br>`string[] grants` | Class, method, parameter, assembly | `Hardened.Web.Testing` in `Hardened.Web.Testing` | Sends the test's requests with these grants, in `X-Test-Grants` | [Sending requests](/guide/testing-web) |
| `[Subject(subject)]`<br>`string subject` | Class, method, parameter, assembly | `Hardened.Web.Testing` in `Hardened.Web.Testing` | Sends the test's requests as this caller, in `X-Test-Subject` | [Sending requests](/guide/testing-web) |
| `[Anonymous]` | Class, method, parameter, assembly | `Hardened.Web.Testing` in `Hardened.Web.Testing` | Sends the test's requests with no credential | [Sending requests](/guide/testing-web) |
| `[PipelineHost]` | Class, method, assembly | `Hardened.Web.Testing` in `Hardened.Web.Testing` | Runs the test on the pipeline host, in the test's own call with no socket | [Test hosts](/guide/testing-hosts) |
| `[KestrelTesting]` | Assembly; repeatable | `Hardened.Web.Kestrel.Testing` in `Hardened.Web.Kestrel.Testing` | Runs a test that carries `[KestrelRuntime]` on Kestrel, on a loopback port | [Test hosts](/guide/testing-hosts) |
| `[AspNetCoreTesting]`, `[AspNetCoreTesting(composition)]`<br>`Type composition`, an `IAspNetCoreTestComposition` | Assembly; repeatable | `Hardened.Web.AspNetCore.Testing` in `Hardened.Web.AspNetCore.Testing` | Runs a test that carries `[AspNetCoreRuntime]` inside the ASP.NET Core pipeline, on Kestrel, on a loopback port | [Test hosts](/guide/testing-hosts) |
| `[KiotaTesting]` | Assembly; repeatable | `Hardened.Kiota.Testing` in `Hardened.Kiota.Testing` | Makes each Kiota client a test parameter, built over the test's host | [Typed clients](/guide/testing-clients) |
| `[RefitTesting]` | Assembly; repeatable | `Hardened.Refit.Testing` in `Hardened.Refit.Testing` | Makes each Refit interface a test parameter, built over the test's host | [Typed clients](/guide/testing-clients) |
| `[TestClientRoute(routeType)]`<br>`Type routeType`, an `ITestClientRoute` | Assembly; repeatable | `Hardened.Web.Testing` in `Hardened.Web.Testing` | Names a route of your own that builds typed clients for the assembly's tests. `[KiotaTesting]` and `[RefitTesting]` derive from it | [Typed clients](/guide/testing-clients) |
| `[TestCredential]`<br>No public constructor | Any; abstract | `Hardened.Web.Testing` in `Hardened.Web.Testing` | The base class of `[Grants]`, `[Subject]` and `[Anonymous]` | None |
| `[TestHost]`<br>No public constructor | Class, method, assembly; abstract | `Hardened.Web.Testing` in `Hardened.Web.Testing` | The base class of an attribute that names a test's host, such as `[PipelineHost]` | None |
| `[TestHostProvider]`<br>No public constructor | Assembly; repeatable; abstract | `Hardened.Web.Testing` in `Hardened.Web.Testing` | The base class of an attribute that names the host a runtime attribute on a test stands for, such as `[KestrelTesting]` | None |

The `[ModuleTest]` attributes come from DependencyModules. A test project references one runner package. The xUnit attribute derives from xUnit's `FactAttribute`. The NUnit attribute is an NUnit test builder. It has no settable property.

`[KiotaTesting]` and `[RefitTesting]` take their targets from `TestClientRouteAttribute`. `[KestrelTesting]` and `[AspNetCoreTesting]` take theirs from `TestHostProviderAttribute`. `IAspNetCoreTestComposition` is in `Hardened.Web.AspNetCore.Testing`. `ITestClientRoute` is in `Hardened.Web.Testing`.

The attributes in the next table come from DependencyModules. `Hardened.Shared.Testing` depends on `DependencyModules.Testing` 1.7.0. A test project references one of the three mock library packages itself. The `hardened-web` template's test project references `DependencyModules.NSubstitute`.

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[Mock]` | Parameter; repeatable | `DependencyModules.Testing.Attributes` in `DependencyModules.Testing` | Registers a test double for the parameter's type in the test's container, and passes it to the test | [Substituting services](/guide/testing-mocks) |
| `[Shared]` | Parameter | `DependencyModules.Testing.Attributes` in `DependencyModules.Testing` | On an `ITestWebApp` or `HttpClient` parameter: sends every request it makes to the test's own container | [Writing a test](/guide/testing) |
| `[TestExport(service)]`<br>`Type service` | Class, method, assembly; repeatable | `DependencyModules.Testing.Attributes` in `DependencyModules.Testing` | Registers a class of your own for every test it covers<br>Properties: `Implementation` (none), `Lifetime` (`ServiceLifetime.Transient`), `Shared` (`false`) | [Substituting services](/guide/testing-mocks) |
| `[NSubstituteSupport]` | Class, method, assembly | `DependencyModules.NSubstitute` in `DependencyModules.NSubstitute` | Makes `[Mock]` build its doubles with NSubstitute | [Substituting services](/guide/testing-mocks) |
| `[MoqSupport]` | Class, method, assembly | `DependencyModules.Moq` in `DependencyModules.Moq` | Makes `[Mock]` build its doubles with Moq | [Substituting services](/guide/testing-mocks) |
| `[FakeItEasySupport]` | Class, method, assembly | `DependencyModules.FakeItEasy` in `DependencyModules.FakeItEasy` | Makes `[Mock]` build its doubles with FakeItEasy | [Substituting services](/guide/testing-mocks) |

## Triggers

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[Queue(name)]`<br>`string name` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method a handler for the messages from a queue. Routes as `QUEUE /{name}` | [Triggers](/guide/triggers) |
| `[Topic(name)]`<br>`string name` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method a handler for the messages published to a topic. Routes as `TOPIC /{name}` | [Triggers](/guide/triggers) |
| `[Timer(name)]`<br>`string name` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method run each time a schedule fires. Routes as `TIMER /{name}` | [Triggers](/guide/triggers) |
| `[Event(source, detailType)]`<br>`string source`, `string detailType` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method a handler for an event from an event bus: `source` is who published it, `detailType` what happened. Routes as `EVENT /{source}/{detailType}` | [Triggers](/guide/triggers) |
| `[Change(name)]`<br>`string name` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method a handler for a row or document that changed. Routes as `CHANGE /{name}` | [Triggers](/guide/triggers) |
| `[Stream(name)]`<br>`string name` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method a handler for records from a sharded stream. Routes as `STREAM /{name}` | [Triggers](/guide/triggers) |
| `[Blob(name)]`<br>`string name` | Any | `Hardened.Functions.Runtime.Attributes` in `Hardened.Functions.Runtime` | Makes the method a handler for a notification that an object changed. Routes as `BLOB /{name}` | [Triggers](/guide/triggers) |
| `[HardenedFunction(functionName)]`<br>`string? functionName = null`, which uses the method's name | Any | `Hardened.Requests.Abstract.Attributes` in `Hardened.Requests.Abstract` | Makes the method a handler for a direct invocation, whose caller gets the return value. Routes as `INVOKE /{functionName}` | [Triggers](/guide/triggers) |

The build reads the trigger attributes on a method. `Hardened.Functions.Runtime` references no cloud. The adapter package that the project references decides which cloud service each trigger receives from. [AWS](#aws), [Google Cloud](#google-cloud) and [Azure](#azure) name each adapter's module attribute.

Each cloud's adapter package registers its module from a build property. An application does not write the module attribute to get the adapter. It writes the attribute to change a setting, such as `[SqsModule(ReportBatchItemFailures = true)]`.

## Testing functions

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[FunctionTesting]` | Class, method, assembly | `Hardened.Functions.Testing` in `Hardened.Functions.Testing` | Makes the trigger façades test parameters. A test sends messages to its handlers through them | [Testing functions](/guide/testing-functions) |
| `[PipelineDelivery]` | Class, method | `Hardened.Functions.Testing` in `Hardened.Functions.Testing` | Puts the tests it covers back on the neutral delivery, where the assembly declares a cloud's testing attribute | [Testing functions](/guide/testing-functions) |
| `[LambdaTesting]` | Class, method, assembly | `Hardened.Aws.Lambda.Testing` in `Hardened.Aws.Lambda.Testing` | Delivers each façade call through the function's `LambdaInvocationHandler`, as the AWS event | AWS [Testing](/aws/testing) |
| `[CloudRunTesting]` | Class, method, assembly | `Hardened.Gcp.CloudRun.Testing` in `Hardened.Gcp.CloudRun.Testing` | Builds each façade call into the request its Google Cloud source sends | Google Cloud [Testing](/gcp/testing) |
| `[AzureFunctionsTesting]` | Class, method, assembly | `Hardened.Azure.Functions.Testing` in `Hardened.Azure.Functions.Testing` | Builds each façade call into the trigger data the isolated worker receives | Azure [Testing](/azure/testing) |

Without a cloud's testing attribute, a façade call is built as a request. The request runs the pipeline with no envelope and no host. The `hardened-function` template's test project declares `[assembly: FunctionTesting]` and the chosen cloud's attribute.

## AWS

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[LambdaHttpModule]` | Module attribute | `Hardened.Aws.Lambda.Http` in `Hardened.Aws.Lambda.Http` | Serves the routes from Lambda, behind an API Gateway HTTP API or a function URL | AWS [Web applications](/aws/lambda-web) |
| `[InvokeModule]` | Module attribute | `Hardened.Aws.Lambda.Invoke` in `Hardened.Aws.Lambda.Invoke` | The adapter for `[HardenedFunction]`, invoked directly | AWS [Invocations](/aws/invoke) |
| `[SqsModule]` | Module attribute | `Hardened.Aws.Lambda.Sqs` in `Hardened.Aws.Lambda.Sqs` | The adapter for `[Queue]`, from Amazon SQS<br>Property: `ReportBatchItemFailures` (`false`) | AWS [Queues](/aws/queue) |
| `[SnsModule]` | Module attribute | `Hardened.Aws.Lambda.Sns` in `Hardened.Aws.Lambda.Sns` | The adapter for `[Topic]`, from Amazon SNS | AWS [Topics](/aws/topic) |
| `[EventBridgeModule]` | Module attribute | `Hardened.Aws.Lambda.EventBridge` in `Hardened.Aws.Lambda.EventBridge` | The adapter for `[Timer]` and `[Event]`, from Amazon EventBridge | AWS [Overview](/aws/) |
| `[DynamoDbStreamsModule]` | Module attribute | `Hardened.Aws.Lambda.DynamoDb` in `Hardened.Aws.Lambda.DynamoDb` | The adapter for `[Change]`, from DynamoDB Streams<br>Property: `ReportBatchItemFailures` (`false`) | AWS [Changes](/aws/change) |
| `[NewImage]` | Parameter | `Hardened.Aws.Lambda.DynamoDb` in `Hardened.Aws.Lambda.DynamoDb` | Binds the row as it is after the change, in DynamoDB's own form, type tags and all | AWS [Changes](/aws/change) |
| `[OldImage]` | Parameter | `Hardened.Aws.Lambda.DynamoDb` in `Hardened.Aws.Lambda.DynamoDb` | Binds the row as it was before the change, in DynamoDB's own form, type tags and all | AWS [Changes](/aws/change) |
| `[KinesisModule]` | Module attribute | `Hardened.Aws.Lambda.Kinesis` in `Hardened.Aws.Lambda.Kinesis` | The adapter for `[Stream]`, from Kinesis Data Streams<br>Property: `ReportBatchItemFailures` (`false`) | AWS [Streams](/aws/stream) |
| `[S3Module]` | Module attribute | `Hardened.Aws.Lambda.S3` in `Hardened.Aws.Lambda.S3` | The adapter for `[Blob]`, from S3 object notifications | AWS [Blobs](/aws/blob) |
| `[LambdaRuntimeModule]` | Module attribute | `Hardened.Aws.Lambda.Runtime.Modules` in `Hardened.Aws.Lambda.Runtime` | The invocation loop that every Lambda function runs. Every adapter's module imports it | AWS [Overview](/aws/) |
| `[LambdaWebTesting]` | Class, method, assembly | `Hardened.Aws.Lambda.Testing` in `Hardened.Aws.Lambda.Testing` | Runs web tests through the function's `LambdaInvocationHandler`, as API Gateway payload format 2.0 events<br>Property: `ResponseMode` (`LambdaResponseMode.Buffered`) | AWS [Testing](/aws/testing) |
| `[DynamoDbClientModule]` | Module attribute | `Hardened.Aws.DynamoDbClient` in `Hardened.Aws.DynamoDbClient` | Registers `IDynamoDbClientProvider`, on any host | [DynamoDB client](/aws/dynamodb) |
| `[LocalDynamoDb]` | Any | `Hardened.Aws.DynamoDbClient.Testing` in `Hardened.Aws.DynamoDbClient.Testing` | On a test: points `IDynamoDbClientProvider` at DynamoDB Local in a Docker container. A derived class creates the tables<br>Property: `Image` (`amazon/dynamodb-local:latest`) | [DynamoDB client](/aws/dynamodb) |

`LambdaResponseMode` is in `Hardened.Aws.Lambda.Runtime.Streaming`. Its values are `Buffered` and `Stream`. `[LambdaTesting]` is under [Testing functions](#testing-functions).

## Google Cloud

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[CloudRunRuntime]` | Module attribute | `Hardened.Gcp.CloudRun.Runtime` in `Hardened.Gcp.CloudRun.Runtime` | Runs the application as a Cloud Run service on Kestrel, and serves trigger handlers beside the routes | [Web services](/gcp/web) |
| `[InvokeModule]` | Module attribute | `Hardened.Gcp.CloudRun.Invoke` in `Hardened.Gcp.CloudRun.Invoke` | The adapter for `[HardenedFunction]`, invoked by a POST to the service<br>Property: `Prefix` (`/_triggers/invoke/`) | Google Cloud [Invocations](/gcp/invoke) |
| `[PubSubModule]` | Module attribute | `Hardened.Gcp.CloudRun.PubSub` in `Hardened.Gcp.CloudRun.PubSub` | The adapter for `[Queue]`, from a Pub/Sub push subscription, and `[Topic]`, from an Eventarc trigger on a Pub/Sub topic | Google Cloud [Overview](/gcp/) |
| `[SchedulerModule]` | Module attribute | `Hardened.Gcp.CloudRun.Scheduler` in `Hardened.Gcp.CloudRun.Scheduler` | The adapter for `[Timer]`, from a Cloud Scheduler job<br>Property: `Prefix` (`/_triggers/timer/`) | Google Cloud [Timers](/gcp/timer) |
| `[EventarcModule]` | Module attribute | `Hardened.Gcp.CloudRun.Eventarc` in `Hardened.Gcp.CloudRun.Eventarc` | The adapter for `[Event]`, any CloudEvent that Eventarc delivers | Google Cloud [Events](/gcp/event) |
| `[FirestoreModule]` | Module attribute | `Hardened.Gcp.CloudRun.Firestore` in `Hardened.Gcp.CloudRun.Firestore` | The adapter for `[Change]`, from Firestore through Eventarc | Google Cloud [Changes](/gcp/change) |
| `[OldValue]` | Parameter | `Hardened.Gcp.CloudRun.Firestore` in `Hardened.Gcp.CloudRun.Firestore` | Binds the document as it was before the change | Google Cloud [Changes](/gcp/change) |
| `[StorageModule]` | Module attribute | `Hardened.Gcp.CloudRun.Storage` in `Hardened.Gcp.CloudRun.Storage` | The adapter for `[Blob]`, from Cloud Storage through Eventarc or a Pub/Sub notification | Google Cloud [Blobs](/gcp/blob) |

`[CloudRunRuntime]` imports `[KestrelRuntime]`. The Cloud Functions host runs a `[CloudRunRuntime]` application. It has no attribute of its own. The two `Prefix` defaults are `InvokeEnvelope.DefaultPrefix` and `SchedulerEnvelope.DefaultPrefix`. They apply when the attribute leaves `Prefix` unset. `[CloudRunTesting]` is under [Testing functions](#testing-functions).

## Azure

| Attribute | Targets | Namespace and package | What it does | Page |
|---|---|---|---|---|
| `[HttpModule]` | Module attribute | `Hardened.Azure.Functions.Http` in `Hardened.Azure.Functions.Http` | Serves the routes from Azure Functions, through one HTTP function named `Http` | Azure [Web applications](/azure/web) |
| `[ServiceBusModule]` | Module attribute | `Hardened.Azure.Functions.ServiceBus` in `Hardened.Azure.Functions.ServiceBus` | The adapter for `[Queue]`, from a Service Bus queue, and `[Topic]`, from a subscription of a Service Bus topic<br>Properties: `Connection` (unset: the app setting `AzureWebJobsServiceBus`), `ReportsItemFailures` (`false`), `Subscription` (unset; a `[Topic]` handler needs it) | Azure [Queues](/azure/queue) |
| `[TimerModule]` | Module attribute | `Hardened.Azure.Functions.Timer` in `Hardened.Azure.Functions.Timer` | The adapter for `[Timer]`, from a timer trigger | Azure [Timers](/azure/timer) |
| `[EventGridModule]` | Module attribute | `Hardened.Azure.Functions.EventGrid` in `Hardened.Azure.Functions.EventGrid` | The adapter for `[Event]`, from Event Grid in the CloudEvents schema | Azure [Events](/azure/event) |
| `[CosmosDbModule]` | Module attribute | `Hardened.Azure.Functions.CosmosDb` in `Hardened.Azure.Functions.CosmosDb` | The adapter for `[Change]`, from the Cosmos DB change feed<br>Properties: `Database` (required), `Connection` (unset: the app setting `CosmosDB`), `LeaseContainer` (unset: `leases`), `RetryCount` and `RetryDelay` (unset: no retry policy) | Azure [Changes](/azure/change) |
| `[EventHubsModule]` | Module attribute | `Hardened.Azure.Functions.EventHubs` in `Hardened.Azure.Functions.EventHubs` | The adapter for `[Stream]`, from Event Hubs<br>Properties: `Connection` (unset: the app setting `AzureWebJobsEventHubs`), `ConsumerGroup` (unset: `$Default`), `RetryCount` and `RetryDelay` (unset: no retry policy) | Azure [Streams](/azure/stream) |
| `[BlobsModule]` | Module attribute | `Hardened.Azure.Functions.Blobs` in `Hardened.Azure.Functions.Blobs` | The adapter for `[Blob]`, from Blob Storage through Event Grid<br>Property: `Connection` (unset: the app setting `AzureWebJobsStorage`) | Azure [Blobs](/azure/blob) |
| `[FunctionsRuntimeModule]` | Module attribute | `Hardened.Azure.Functions.Runtime.Modules` in `Hardened.Azure.Functions.Runtime` | Runs each invocation. Every adapter's module imports it | Azure [Overview](/azure/) |
| `[AzureFunctionsWebTesting]` | Class, method, assembly | `Hardened.Azure.Functions.Testing` in `Hardened.Azure.Functions.Testing` | Runs web tests through the app's `FunctionsInvocationHandler`, with no Functions host | Azure [Testing](/azure/testing) |

The Azure build reads the module attributes' settings from the application class. It writes them into the function metadata. `[CosmosDbModule]` without `Database` fails the build with `HRDAZ003`. A `[Topic]` handler fails the build with the same diagnostic when `[ServiceBusModule]` sets no `Subscription`.

When `Connection` is unset, the build names no connection for Service Bus, Cosmos DB and Blob Storage. The Azure extension's default setting then applies. For Event Hubs the build writes `AzureWebJobsEventHubs`. The defaults `AzureWebJobsServiceBus`, `CosmosDB`, `leases`, `$Default` and `AzureWebJobsStorage` are the Azure extensions' own.

`[AzureFunctionsTesting]` is under [Testing functions](#testing-functions).

## Limits

On a `[HardenedModule]` class, an attribute with an enum argument or enum property set to a value other than the enum's zero value fails the build. The error is in the generated `<Module>.Module.g.cs`. It is `CS1503` for a constructor argument and `CS0266` for a named property. These attributes fail the build on a module class with the values shown:

| Attribute on the module class | Error |
|---|---|
| `[ContentNegotiation(ContentNegotiationMode.Lenient)]` | `CS1503` |
| `[ResponseModel(ResponseModel.Response)]` | `CS1503` |
| `[ValidationMode(ValidationStopMode.StopOnFirstError)]` | `CS1503` |
| `[RateLimit(Scope = RateLimitScope.Principal)]` | `CS0266` |
| `[Compress(Favor = CompressionType.Br)]` | `CS0266` |
| `[CacheControl(Type = CacheControlEnum.NoStore)]` | `CS0266` |
| `[CacheResponse<VaryByRoute>(Scope = CacheScope.PerCaller)]` | `CS0266` |

The filter attributes in the table compile with the same values off the module. `[RateLimit]` compiles with its value on a controller class. `[Compress]`, `[CacheControl]` and `[CacheResponse<TProvider>]` compile with theirs on a handler method. `[ValidationMode]` compiles with `StopOnFirstError` on either. `[RateLimit(Scope = RateLimitScope.Transport)]` sets the zero value and compiles on the module class.

`[ContentNegotiation]` sets nothing in either placement it accepts. `Lenient` on a module class fails the build. On an assembly the attribute has no effect.

## Next

- [Modules](/guide/modules): module attributes, imports and module properties
- [The execution pipeline](/guide/execution-pipeline): filter attributes on a method, a class or a module, and writing your own
- [Triggers](/guide/triggers): the trigger attributes and the adapter on each cloud
- [Packages](/reference/packages): every package and what it ships
- [Diagnostics](/reference/diagnostics): the build's diagnostics, such as `HRDR004` and `HRDAZ003`
