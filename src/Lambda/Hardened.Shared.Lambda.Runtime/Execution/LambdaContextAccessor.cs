using Amazon.Lambda.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Shared.Lambda.Runtime.Execution
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
