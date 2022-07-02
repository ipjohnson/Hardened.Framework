using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Errors
{
    public interface IExceptionThrownHandler
    {
        Task Handle(IExecutionContext context, Exception exception);
    }
}
