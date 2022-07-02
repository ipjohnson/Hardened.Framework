using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace Hardened.Templates.Abstract
{
    public interface ITemplateHelper
    {
        ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments);
    }
}
