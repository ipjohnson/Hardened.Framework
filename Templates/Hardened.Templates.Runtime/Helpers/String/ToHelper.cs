using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class ToHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            if (arguments.Length == 0)
            {
                return new ValueTask<object>(null);
            }

            if (arguments.Length >= 2 && 
                arguments[0] is IFormattable formattable &&
                arguments[1] is string formatString)
            {
                return new ValueTask<object>(formattable.ToString(formatString, null));
            }

            return new ValueTask<object>(arguments[0]?.ToString());
        }
    }
}
