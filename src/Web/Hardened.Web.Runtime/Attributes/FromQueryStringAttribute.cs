using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Web.Runtime.Attributes
{
    public class FromQueryStringAttribute : Attribute
    {
        public FromQueryStringAttribute(string? name = null)
        {
            Name = name;
        }

        public string? Name { get; }
    }
}
