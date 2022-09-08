using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public abstract class BaseStringEvaluationHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            var returnValue = false;

            if (arguments?.Length == 2)
            {
                var stringOne = arguments[0]?.ToString();

                if (stringOne != null)
                {
                    var stringTwo = arguments[1]?.ToString();

                    if (stringTwo != null)
                    {
                        returnValue = EvaluateStrings(stringOne, stringTwo);
                    }
                }
            }

            return new ValueTask<object>(returnValue);
        }

        protected abstract bool EvaluateStrings(string one, string two);
    }
}
