using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Runtime.Attributes
{
    public class EnvironmentVariableAttribute : Attribute
    {
        public EnvironmentVariableAttribute(string variable)
        {
            Variable = variable;
        }

        public string Variable { get; }
    }
}
