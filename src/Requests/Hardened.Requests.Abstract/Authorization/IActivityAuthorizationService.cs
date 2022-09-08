using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Authorization
{
    public interface IActivityAuthorizationService
    {
        ValueTask<bool?> Authorize(IExecutionContext context, params string[] actions);
    }
}
