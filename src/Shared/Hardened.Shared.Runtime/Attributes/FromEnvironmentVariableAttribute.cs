using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Attributes
{
    /// <summary>
    /// Attribute ConfigurationModel properties to populate from environment
    /// </summary>
    public class FromEnvironmentVariableAttribute : Attribute
    {
        public FromEnvironmentVariableAttribute(string environmentVariable, string defaultValue = "")
        {
            EnvironmentVariable = environmentVariable;
            DefaultValue = defaultValue;
        }

        public string EnvironmentVariable { get; }

        public string DefaultValue { get; }
    }
}
