using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Application
{
    public class EnvironmentImpl : IEnvironment
    {
        public EnvironmentImpl()
        {
            Name = System.Environment.GetEnvironmentVariable("HARDENED_ENVIRONMENT") ?? "development";
        }
        
        public string Name { get; }

        public T? Value<T>(string name, T? defaultValue = default)
        {
            var envValue = Environment.GetEnvironmentVariable(name);

            if (!string.IsNullOrEmpty(envValue))
            {
                if (typeof(T) == typeof(string))
                {
                    return (T)(object)envValue;
                }

                return (T)Convert.ChangeType(envValue, typeof(T));
            }

            return defaultValue;
        }
    }
}
