# CORS

Hardened answers cross-origin requests from a `CorsConfiguration`. It covers every request until a
route declares `[Cors]`. From then on it covers only the routes that declare it.

```csharp
using Hardened.Web.Runtime.Cors;

[Cors]
[BasePath("/widgets")]
public class WidgetController {

    [Get("/{id}")]
    public Widget Read(string id) => _widgets.Read(id);
}
```

## Allowing origins

The web module reads `CORS_ALLOWED_ORIGINS` when it builds its `CorsConfiguration`. The value is a
comma-separated list of origins.

| Entry | Allows |
|---|---|
| `https://app.example.com` | that origin |
| `*.example.com` | every subdomain of `example.com`, and not `example.com` itself |
| `*` | every origin |

An application that configures CORS in code registers its own `CorsConfiguration`. It registers
it after the web module, and the container resolves the last registration.

```csharp
public void ConfigureServices(IServiceCollection services)
{
    services.AddSingleton(_ =>
    {
        var cors = new CorsConfiguration { AllowCredentials = true };

        cors.AllowOrigin("https://app.example.com");
        cors.AllowHeader("X-Tenant");

        return cors;
    });
}
```

| Member | Default | Effect |
|---|---|---|
| `AllowOrigin(origin)` | none | Allows one origin |
| `AllowOriginSuffix(domain)` | none | Allows every subdomain of `domain` |
| `AllowAnyOrigin` | `false` | Allows every origin |
| `AllowCredentials` | `false` | Sends `Access-Control-Allow-Credentials: true` |
| `AllowHeader(header)` | `Authorization`, `Content-Type`, `Accept`, `x-auth-token`, `x-amz-content-sha256` | Adds a request header a preflight may ask for |
| `ExposeHeader(header)` | the correlation header | Adds a response header a script may read |
| `ClearExposedHeaders()` | | Exposes nothing |
| `MaxAgeSec` | `86400` | How long a browser may cache a preflight |
| `FallbackMethods` | `GET, POST, PUT, DELETE, OPTIONS` | The verbs a preflight is told about when no routing table knows the path |

Under `AllowAnyOrigin` the allow header is `*`. With `AllowCredentials` as well, it echoes the
request's origin instead, and `Access-Control-Allow-Credentials` is not sent. The specification
forbids credentials with a `*` origin.

## What a request gets

A request that carries `Origin` gets `Vary: Origin`, whether or not the origin is allowed. An
allowed origin also gets `Access-Control-Allow-Origin`. It gets `Access-Control-Allow-Credentials`
under `AllowCredentials`, and `Access-Control-Expose-Headers` when anything is exposed.

A preflight is an `OPTIONS` that carries `Access-Control-Request-Method`. The middleware answers it
with 204 before any handler runs. An allowed preflight names the verb it asked about in
`Access-Control-Allow-Methods`, once the routing table has that verb at the path. It also carries
`Access-Control-Max-Age`, and `Access-Control-Allow-Headers` naming the headers it asked about. A
refused preflight is a 204 without those headers, which tells the browser not to send the request.
A preflight is refused when the path's routes lack the verb, or when it asks for a header outside
`AllowHeader`.

An `OPTIONS` without `Access-Control-Request-Method` is not a preflight, and it reaches the route.

## CORS on some routes

`[Cors]` goes on the handler method, on its class, or on a `[HardenedModule]` class, where it covers
every handler compiled with it. The nearest declaration wins.

Once a route or a module declares it, the build registers `CorsManifest`, and CORS covers only the
routes a declaration reaches. A cross-origin request to any other route is answered as if the
application had no CORS, and a preflight for it is refused. So is a path no route answers, such as
static content. An application that declares `[Cors]` nowhere keeps CORS on every request.

The filter runs ahead of every stage that can refuse a request. A 401, a 429 or a validation 400
on a declaring route carries the headers a browser needs before a script may read it.

## A policy for some routes

`[Cors<TPolicy>]` answers with a policy registered for `TPolicy` instead of the application's
`CorsConfiguration`. The type only names the policy, so an empty class will do.

```csharp
public sealed class Partners;

services.AddCorsPolicy<Partners>(policy => policy.AllowOrigin("https://partner.example.com"));

[Cors<Partners>]
[BasePath("/partner")]
public class PartnerController { ... }
```

A named policy starts empty. It does not read `CORS_ALLOWED_ORIGINS`, which configures the
application's policy only. A policy that nothing registered fails the route's first request with
`InvalidOperationException`, and the message names the attribute.

## Next

- [Authentication](/guide/authentication)
- [The execution pipeline](/guide/execution-pipeline)
- [Attributes](/reference/attributes)
