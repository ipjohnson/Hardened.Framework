# A shipped bearer principal source

> **Status.** Design, not built. The seam it plugs into shipped with the authentication
> middleware: `IPrincipalSource`, run ahead of the handler chain, first answer onto
> `IExecutionContext.CallerPrincipal`. This document describes the first source the framework
> would ship. Nothing here is API until it is.

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
- The delegate's null means the credential was refused; the request continues anonymously and
  authorization refuses it with the challenge it already composes. A delegate that wants the
  RFC 6750 `error="invalid_token"` answer throws `AuthorizationException` with
  `AuthorizationChallenge.InsufficientAuthentication()` or a challenge of its own.

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
   API.
2. Whether `AuthorizationFilter`'s `AuthenticationRequired()` challenge should name the wire
   scheme of the operation's declared scheme type rather than defaulting to `Bearer`. It is
   right by accident today for the only source this document proposes.
3. Whether the delegate receives the raw header value or the token with the scheme word
   stripped. Stripped is proposed above; a source for a proprietary header shape is a different
   source.
