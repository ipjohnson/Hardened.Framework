using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Attributes;

public interface ICustomBindingAttribute {
    ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter);
}