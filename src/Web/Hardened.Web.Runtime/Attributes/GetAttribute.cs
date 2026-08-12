namespace Hardened.Web.Runtime.Attributes;

/// <summary>
/// Routes a GET request to the attributed handler.
///
/// <para>
/// This carried <c>SuccessStatus</c>, <c>ValidationErrorStatus</c>, <c>NullReturnStatus</c> and
/// <c>ErrorStatus</c> until 2026-08-11. Nothing read them: the web generator's
/// <c>RequestHandlerNameModel</c> carries only path and method, the emitted
/// <c>ExecutionRequestHandlerInfo</c> never set the status overrides, and they fell back to the
/// interface's null defaults. Setting one changed nothing, and the declared defaults did not even
/// describe what the runtime does — <c>NullReturnStatus = 404</c> shipped on <c>[Delete]</c> while
/// <c>NullValueResponseHandler</c> answers a null DELETE with 200. Removed rather than wired: see
/// docs/TESTING-PLAN.md §2.3.
/// </para>
/// </summary>
public class GetAttribute : Attribute {
    public GetAttribute(string path = "") {
        Path = path;
    }

    public string Path { get; }
}
