using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing
{

    [AttributeUsage(AttributeTargets.Parameter)]
    public class MockAttribute : Attribute, ITestServiceProvider
    {
        private Type _mockType = typeof(object);

        public void InitializeProvider(object initValue)
        {
            if (initValue is ParameterInfo parameterInfo)
            {
                _mockType = parameterInfo.ParameterType;
            }
            else
            {
                throw new Exception("MockAttribute initialize on something other than parameter");
            }
        }

        public void RegisterService(IServiceCollection collection)
        {
            var instance =
                NSubstitute.Substitute.For(new[] { _mockType }, Array.Empty<object>());

            collection.AddTransient(_mockType, _ => instance);
        }
    }
}
