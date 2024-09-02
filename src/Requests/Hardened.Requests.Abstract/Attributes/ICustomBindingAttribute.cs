using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Attributes;

public interface ICustomBindingAttribute {
    Task<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter);
}