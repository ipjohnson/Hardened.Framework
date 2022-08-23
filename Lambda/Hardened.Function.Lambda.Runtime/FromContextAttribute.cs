using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Function.Lambda.Runtime
{
    public class FromContextAttribute : Attribute
    {
        public FromContextAttribute(string? name = null)
        {
            Name = name;
        }

        public string? Name { get; }
    }
}
