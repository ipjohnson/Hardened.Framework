using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Hardened.Shared.Testing
{
    [AttributeUsage(AttributeTargets.Method)]
    public class TestExposeMethodAttribute : Attribute, ITestExposeAttribute
    {
        public TestExposeMethodAttribute(string method)
        {
            Method = method;
        }

        public TestExposeMethodAttribute(Type type, string method)
        {
            Type = type;
            Method = method;
        }

        public Type? Type { get; }

        public string Method { get; }

        void ITestExposeAttribute.ExposeDependencies(MethodInfo method, IServiceCollection services)
        {
            var staticType = Type;

            if (staticType == null)
            {
                staticType = method.DeclaringType;
            }

            MethodInfo? dependencyMethod = null;

            if (staticType != null)
            {
                dependencyMethod = staticType.GetMethods().FirstOrDefault(m => m.Name == Method); ;
            }

            if (dependencyMethod == null)
            {
                throw new Exception($"Could not locate dependency method {Method} on {staticType?.FullName}");
            }

            dependencyMethod.Invoke(null, new object[] { method, services });
        }
    }
}
