# A shipped bearer principal source

> **Status.** Built for #462. `BearerPrincipalSource<TScheme>` is in `Hardened.Requests.Runtime`,
> and `Hardened.Requests.Jwt` is the JWT package this document said could follow. Two things
> changed on the way. A refused token is `AnonymousCallerPrincipal.Rejected`, which
> `AuthorizationFilter` answers with `error="invalid_token"`, so a source can say a token was bad
> without refusing the request itself. And the open questions below are answered in place.

## Why ship one at all

The seam alone reproduces the situation it replaced, one layer up. Every application terminating
bearer tokens writes the same twenty lines: read the `Authorization` header, check the scheme
word, hand the token to whatever validates it, build a principal. Three arms of the 0.17 trial
wrote exactly that middleware independently, and both in-repo fixtures carried a copy. The
testing source (`TestGrantsPrincipalSource`) already ships for tests; this is its production
sibling.

## Shape

One class, delegate-validated, no cryptography dependency:

```csharp
public sealed class BearerPrincipalSource<TScheme> : IPrincipalSource<TScheme>
    where TScheme : IAuthenticationScheme {

    public BearerPrincipalSource(
        Func<string, IExecutionContext, ValueTask<ICallerPrincipal?>> validate) { ... }

    public ValueTask<ICallerPrincipal?> Authenticate(IExecutionContext context) { ... }
}
```

- Reads `Authorization`. Absent, or a scheme word other than `Bearer`, answers null so the next
  source is asked and an anonymous request stays anonymous.
- A present token goes to the delegate. The delegate owns validation entirely: parse it as a JWT,
  introspect it against an issuer, look it up in a table. The framework never learns which.
- The delegate's null means the credential was refused. The source answers
  `AnonymousCallerPrincipal.Rejected()`, so the request continues anonymously and a requirement
  refuses it with `error="invalid_token"`. A route that requires no caller serves it, as ASP.NET
  Core's `JwtBearer` does. `AuthenticationMiddleware` is unchanged: a source still cannot refuse a
  request itself.

Registration is one line beside the scheme declaration the document already reads:

```csharp
[HttpAuthenticationScheme("bearer", BearerFormat = "JWT")]
public sealed class ApiBearer : IAuthenticationScheme;

services.AddSingleton<IPrincipalSource>(
    new BearerPrincipalSource<ApiBearer>((token, _) => ValidateToken(token)));
```

The type parameter ties the source to the scheme `[Authorize<ApiBearer>]` names and the
published `securitySchemes` entry is keyed by, so "find references" walks from an operation's
requirement to the code that terminates its credential. The runtime does not dispatch on it.

## What it deliberately does not do

- **No JWT dependency.** Signature verification, issuer allowlists and key rotation live behind
  the delegate. A `Hardened.Requests.Jwt` package wrapping
  `Microsoft.IdentityModel.JsonWebTokens` could follow separately; it would be a delegate
  factory, not a second seam.
- **No scheme negotiation.** One source per credential shape, asked in registration order. An
  application with a bearer API and a cookie UI registers two sources.
- **No grant resolution.** The delegate may put grants on the principal it builds - a token's
  scopes map naturally - and `IActivityAuthorizationService` contributors keep working either
  way.

## Open questions

1. The principal's `AuthenticationScheme` string: the wire word (`"bearer"`, matching the
   scheme attribute's argument) or the type name (`"ApiBearer"`, matching the document key).
   The testing source says `"test"` and nothing reads the value yet; whichever ships becomes
   API. **Answered:** the JWT package's principal says `"bearer"`
   (`JwtBearerValidator.SchemeName`). The delegate source leaves it to the delegate.
2. Whether `AuthorizationFilter`'s `AuthenticationRequired()` challenge should name the wire
   scheme of the operation's declared scheme type rather than defaulting to `Bearer`. It is
   right by accident today for the only source this document proposes.
3. Whether the delegate receives the raw header value or the token with the scheme word
   stripped. Stripped is proposed above; a source for a proprietary header shape is a different
   source. **Answered:** stripped, and trimmed.
