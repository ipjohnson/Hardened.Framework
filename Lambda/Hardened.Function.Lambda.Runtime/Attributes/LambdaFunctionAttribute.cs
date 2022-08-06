using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Function.Lambda.Runtime.Attributes
{
    public class LambdaFunctionAttribute : Attribute
    {
        public string FunctionName { get; set; }
    }
}
