namespace Hardened.Requests.Abstract.Responses;

/// <summary>
/// A status marker for every registered code the framework ships no record of its own for.
/// </summary>
/// <remarks>
/// <para>
/// One line each, for <see cref="Status{TCode, TBody}"/> to close over. A record per status would
/// be a shape decided before anything produces it; a marker decides nothing except the number,
/// which is the only fact about these codes the framework actually knows.
/// </para>
/// <para>
/// <b>Not a complete list, and it cannot be.</b> 529 is registered nowhere and several services
/// answer with it. An application declares its own marker beside these - a one-line struct
/// implementing <see cref="IStatusCode"/> - and <c>Status&lt;Overloaded, Problem&gt;</c> then works
/// exactly as the ones here do. That is the point of the marker being a type argument rather than
/// a row in a table.
/// </para>
/// <para>
/// The 1xx codes are absent deliberately. An informational response is sent before the final one
/// and is not something a handler returns, so a type for it would describe nothing a handler can
/// do.
/// </para>
/// </remarks>
public static class Http {

    // Successes with no record of their own. 206 waits on static content, which is where a range
    // response is produced - shipping the shape before there is a producer for it fixes it early.

    [HttpStatus(203)]
    public readonly struct NonAuthoritativeInformation : IStatusCode {
        public static int Status => 203;
    }

    [HttpStatus(205)]
    public readonly struct ResetContent : IStatusCode {
        public static int Status => 205;
    }

    [HttpStatus(206)]
    public readonly struct PartialContent : IStatusCode {
        public static int Status => 206;
    }

    [HttpStatus(207)]
    public readonly struct MultiStatus : IStatusCode {
        public static int Status => 207;
    }

    [HttpStatus(208)]
    public readonly struct AlreadyReported : IStatusCode {
        public static int Status => 208;
    }

    [HttpStatus(226)]
    public readonly struct IMUsed : IStatusCode {
        public static int Status => 226;
    }

    // Redirection, which is one decision rather than seven. Every one of these requires a
    // Location and a choice of semantics between them that only the caller can make, so none is
    // shipped as a record and all of them are reachable here.

    [HttpStatus(300)]
    public readonly struct MultipleChoices : IStatusCode {
        public static int Status => 300;
    }

    [HttpStatus(301)]
    public readonly struct MovedPermanently : IStatusCode {
        public static int Status => 301;
    }

    [HttpStatus(302)]
    public readonly struct Found : IStatusCode {
        public static int Status => 302;
    }

    [HttpStatus(303)]
    public readonly struct SeeOther : IStatusCode {
        public static int Status => 303;
    }

    [HttpStatus(305)]
    public readonly struct UseProxy : IStatusCode {
        public static int Status => 305;
    }

    [HttpStatus(307)]
    public readonly struct TemporaryRedirect : IStatusCode {
        public static int Status => 307;
    }

    [HttpStatus(308)]
    public readonly struct PermanentRedirect : IStatusCode {
        public static int Status => 308;
    }

    // Client errors outside the shipped set. 418 is reserved rather than registered, and it is
    // here because a description is entitled to declare it.

    [HttpStatus(407)]
    public readonly struct ProxyAuthenticationRequired : IStatusCode {
        public static int Status => 407;
    }

    [HttpStatus(411)]
    public readonly struct LengthRequired : IStatusCode {
        public static int Status => 411;
    }

    [HttpStatus(414)]
    public readonly struct UriTooLong : IStatusCode {
        public static int Status => 414;
    }

    [HttpStatus(416)]
    public readonly struct RangeNotSatisfiable : IStatusCode {
        public static int Status => 416;
    }

    [HttpStatus(417)]
    public readonly struct ExpectationFailed : IStatusCode {
        public static int Status => 417;
    }

    [HttpStatus(418)]
    public readonly struct ImATeapot : IStatusCode {
        public static int Status => 418;
    }

    [HttpStatus(421)]
    public readonly struct MisdirectedRequest : IStatusCode {
        public static int Status => 421;
    }

    [HttpStatus(423)]
    public readonly struct Locked : IStatusCode {
        public static int Status => 423;
    }

    [HttpStatus(424)]
    public readonly struct FailedDependency : IStatusCode {
        public static int Status => 424;
    }

    [HttpStatus(425)]
    public readonly struct TooEarly : IStatusCode {
        public static int Status => 425;
    }

    [HttpStatus(426)]
    public readonly struct UpgradeRequired : IStatusCode {
        public static int Status => 426;
    }

    [HttpStatus(431)]
    public readonly struct RequestHeaderFieldsTooLarge : IStatusCode {
        public static int Status => 431;
    }

    [HttpStatus(451)]
    public readonly struct UnavailableForLegalReasons : IStatusCode {
        public static int Status => 451;
    }

    // Server errors outside the shipped set.

    [HttpStatus(505)]
    public readonly struct HttpVersionNotSupported : IStatusCode {
        public static int Status => 505;
    }

    [HttpStatus(506)]
    public readonly struct VariantAlsoNegotiates : IStatusCode {
        public static int Status => 506;
    }

    [HttpStatus(507)]
    public readonly struct InsufficientStorage : IStatusCode {
        public static int Status => 507;
    }

    [HttpStatus(508)]
    public readonly struct LoopDetected : IStatusCode {
        public static int Status => 508;
    }

    [HttpStatus(510)]
    public readonly struct NotExtended : IStatusCode {
        public static int Status => 510;
    }

    [HttpStatus(511)]
    public readonly struct NetworkAuthenticationRequired : IStatusCode {
        public static int Status => 511;
    }
}
