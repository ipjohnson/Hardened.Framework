using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Serializer
{
    public interface IExceptionResponseSerializer
    {
        Task Handle(IExecutionContext context, Exception exp);
    }
}
