using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Errors;

public interface IExceptionToModelConverter
{
    (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp);
}