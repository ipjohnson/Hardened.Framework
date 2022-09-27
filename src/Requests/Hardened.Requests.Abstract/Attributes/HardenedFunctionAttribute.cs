using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Requests.Abstract.Attributes
{
    public class HardenedFunctionAttribute : Attribute
    {
        public string? FunctionName { get; set; }
    }
}
