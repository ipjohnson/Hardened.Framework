using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization;

public interface IActivityAuthorizationHandler {
    ValueTask<bool?> Authorize(IExecutionContext context, params string[] actions);
}