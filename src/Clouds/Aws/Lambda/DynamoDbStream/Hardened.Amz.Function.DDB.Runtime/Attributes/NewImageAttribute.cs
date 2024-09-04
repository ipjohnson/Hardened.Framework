using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Amz.Function.DDB.Runtime.Attributes;

public class NewImageAttribute : Attribute, ICustomBindingAttribute {

    public ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) {
        throw new NotImplementedException();
    }
}