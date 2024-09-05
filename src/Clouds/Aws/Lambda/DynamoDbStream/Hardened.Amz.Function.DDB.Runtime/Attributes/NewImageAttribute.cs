using Hardened.Amz.Function.DDB.Runtime.Impl;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Amz.Function.DDB.Runtime.Attributes;

public class NewImageAttribute : Attribute, ICustomBindingAttribute {

    public ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) {
        var recordContext = context.RequestServices.GetRequiredService<CurrentDdbRecordContext>();

        var newImage = recordContext.CurrentRecord.Dynamodb.NewImage;

        if (newImage is T tValue) {
            return new ValueTask<T>(tValue);
        }
        
        throw new InvalidCastException($"OldImage can only be used on Dictionary<string,AttributeValue>.");
    }
}