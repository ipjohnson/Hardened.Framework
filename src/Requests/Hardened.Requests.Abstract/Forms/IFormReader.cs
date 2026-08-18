using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Forms;

/// <summary>
/// Reads a request's form fields.
/// </summary>
/// <remarks>
/// Asynchronous because it reads the body. The generated binder awaits it once per handler and
/// binds every form parameter from the result, the same way it resolves the serialization service
/// once for a body parameter.
/// </remarks>
public interface IFormReader {
    /// <summary>
    /// The form the request carried, or an empty collection when it carried none.
    /// </summary>
    /// <remarks>
    /// Empty rather than null for the wrong content type, an empty body or a verb that has no body
    /// at all - a caller asks what a field was and gets nothing, rather than asking whether there
    /// was a form first.
    /// </remarks>
    ValueTask<IFormCollection> ReadForm(IExecutionContext context);
}
