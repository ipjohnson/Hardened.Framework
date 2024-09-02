using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.IntegrationTests.WebApp.SUT.Filters;

public class TestFilterAttribute : Attribute, ICustomBindingAttribute {
    private readonly string _value;

    public TestFilterAttribute(string value) {
        _value = value;

    }
    
    public async ValueTask<T> BindValue<T>(IExecutionContext context, IExecutionRequestParameter parameter) {
        if (typeof(T) == typeof(string)) {
            return (T)(object)_value;
        }
        
        throw new NotSupportedException("Not supported");
    }
}
