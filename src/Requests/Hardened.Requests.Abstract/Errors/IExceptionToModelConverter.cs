using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Requests.Abstract.Errors
{
    public interface IExceptionToModelConverter
    {
        (int, object) ConvertExceptionToModel(IExecutionContext context, Exception exp);
    }
}
