namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Routes a PUT request to the attributed handler.
/// </summary>
/// <remarks>
/// <para>
/// This carried <c>SuccessStatus</c>, <c>ValidationErrorStatus</c>, <c>NullReturnStatus</c> and
/// <c>ErrorStatus</c> until 2026-08-11, when they were removed rather than wired because nothing
/// read them - and because the defaults they declared were wrong, <c>NullReturnStatus = 404</c>
/// shipping on <c>[Delete]</c> while the runtime answers a null DELETE with 200.
/// </para>
/// <para>
/// <c>SuccessStatus</c> is back, wired, and defaulted to nothing. "Nothing read it" was an argument
/// for reading it, and a specification-first handler can now say the same thing through its
/// description - so leaving the attribute mute would make a hand-written handler the only kind that
/// cannot state the status it answers with. Both reach
/// <c>RequestHandlerModel.ResponseInformation.DefaultStatusCode</c> and there is one runtime
/// behaviour behind them.
/// </para>
/// <para>
/// Unset means 200. The other three are still gone: a validation or error status has no source of
/// truth behind a hand-written assertion, which is what made the original four dead weight.
/// </para>
/// </remarks>
public class PutAttribute : Attribute {
    public PutAttribute(string path = "") {
        Path = path;
    }

    public string Path { get; }

    /// <summary>
    /// The status a successful response carries. Unset answers 200.
    /// </summary>
    /// <remarks>
    /// 201 for a route that creates something, 202 for one that accepts work it has not finished,
    /// 204 for one that answers with no body - the framework writes none for 204, 205 or 304
    /// whatever the handler returned.
    /// </remarks>
    public int SuccessStatus { get; set; }
}
