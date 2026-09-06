$version: "2"

namespace hardened.api

/// How long the server may spend on this operation before it stops and answers.
///
/// Smithy models the exchange and says nothing about how long a server may take over it, because
/// that is a property of the server rather than of the contract between it and its callers. This
/// is Hardened's vocabulary for saying it in the model anyway, so a service generated from a
/// description is bounded the way its author intended rather than only where somebody remembered
/// to write an attribute on the implementation.
///
/// A budget stated here is the operation's own, and the nearest declaration wins: a `[Timeout]` on
/// the generated implementation's method or class overrides it, and this overrides the assembly's
/// and the application's default.
@trait(selector: "operation")
structure timeout {
    /// The budget. Has to be greater than zero; an operation that should not be bounded declares
    /// no timeout at all.
    @required
    milliseconds: Integer

    /// What the caller is told when the budget runs out. 504 unless stated, which is what
    /// ASP.NET Core's request-timeout middleware answers and what a deadline out at a dependency
    /// honestly is. State 503 for an operation shedding load rather than waiting on something.
    status: Integer

    /// Seconds for the `Retry-After` header, or absent for none. Only honest alongside status 503:
    /// a deadline out at a dependency knows nothing about when that dependency recovers.
    retryAfterSeconds: Integer
}

/// States that a member's narrowing to a C# type is what the model intended, so the build stops
/// reporting it.
///
/// `BigDecimal` and `BigInteger` have no exact C# type. `BigDecimal` becomes `decimal`, which is
/// exact and holds 28 significant digits rather than arbitrarily many, and `BigInteger` becomes
/// `long`. Both are reported, because a model that wanted arbitrary precision has lost something
/// and nothing else would say so.
///
/// A model that reached for `BigDecimal` to get exactness rather than range has arrived, and had
/// no way to say so: the only way to a warning-free build was to suppress `HSMT006` for the whole
/// project, which silences the members that did lose something too. This is the member saying it
/// is satisfied.
///
/// It goes on the member, or once on a named shape every such member targets.
@trait(selector: ":is(member, simpleType)")
structure narrowed {}
