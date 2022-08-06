using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Amazon.Lambda.Core;

namespace Hardened.Function.Lambda.Runtime.Execution
{
    public interface ILambdaContextAccessor
    {
        ILambdaContext? Context { get; set; }
    }

    public class LambdaContextAccessor : ILambdaContextAccessor
    {
        public ILambdaContext? Context { get; set; }
    }
}
