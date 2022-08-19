using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Web.Runtime.Attributes
{
    public class FromHeaderAttribute : Attribute
    {
        public string? Name { get; set; }
    }
}
