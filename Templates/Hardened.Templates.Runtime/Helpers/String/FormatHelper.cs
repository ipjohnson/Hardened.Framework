using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class FormatHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            var returnString = string.Empty;

            if (arguments?.Length >= 2)
            {
                var formatString = arguments[0]?.ToString();

                if (formatString != null)
                {
                    returnString = string.Format(formatString, GetArgumentsArray(arguments));
                }
            }

            return new ValueTask<object>(returnString);
        }

        private object[] GetArgumentsArray(object[] arguments)
        {
            var newArray = new object[arguments.Length - 1];

            for (var i = 1; i < arguments.Length; i++)
            {
                newArray[i - 1] = arguments[i];
            }

            return newArray;
        }
    }
}
