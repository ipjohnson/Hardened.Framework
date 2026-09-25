using System.Globalization;
using Hardened.Requests.Abstract.Attributes;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.IntegrationTests.WebApp.SUT.Filters;

/// <summary>
/// Binds the <c>X-Count</c> header with <c>int.Parse</c>, which throws a <c>FormatException</c>
/// while the request is bound when the header is not a number.
/// </summary>
public class CountHeaderAttribute : Attribute, ICustomBindingAttribute
{
    public ValueTask<T> BindValue<T>(
        IExecutionContext context,
        IExecutionRequestParameter parameter
    )
    {
        context.Request.Headers.TryGetValue("X-Count", out var value);

        var count = int.Parse(value.ToString(), CultureInfo.InvariantCulture);

        return new ValueTask<T>((T)(object)count);
    }
}
