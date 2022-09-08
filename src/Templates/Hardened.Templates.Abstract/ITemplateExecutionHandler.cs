using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;

namespace Hardened.Templates.Abstract
{
    public interface ITemplateExecutionHandler
    {
        Task Execute(
            object requestValue,
            IServiceProvider serviceProvider,
            ITemplateOutputWriter writer,
            ITemplateExecutionContext? parentContext, 
            IExecutionContext? executionContext);
    }
}
