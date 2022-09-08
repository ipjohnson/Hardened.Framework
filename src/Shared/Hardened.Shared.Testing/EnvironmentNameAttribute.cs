using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Testing
{
    public class EnvironmentNameAttribute : Attribute
    {
        public EnvironmentNameAttribute(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }
}
