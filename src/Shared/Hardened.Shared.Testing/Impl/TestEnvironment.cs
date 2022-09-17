using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Shared.Runtime.Application;

namespace Hardened.Shared.Testing.Impl
{
    public class TestEnvironment : IEnvironment
    {
        private readonly IDictionary<string, object> _values;

        public TestEnvironment(string name, IDictionary<string, object> values)
        {
            _values = values;
            Name = name;
        }

        public string Name { get; }

        public T? Value<T>(string name, T? defaultValue = default)
        {
            if (_values.TryGetValue(name, out var value))
            {
                if (value is T tValue)
                {
                    return tValue;
                }

                return (T)Convert.ChangeType(value, typeof(T));
            }

            return defaultValue;
        }
    }
}
