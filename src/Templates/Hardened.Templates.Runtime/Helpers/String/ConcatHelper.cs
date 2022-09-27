using System.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public class ConcatHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            if (arguments.Length == 1)
            {
                if (arguments[0] is IEnumerable enumerable and not string)
                {
                    return ConcatEnumerable(handlerDataContext, enumerable);
                }
            }

            return ConcatEnumerable(handlerDataContext, arguments);
        }
        

        private ValueTask<object> ConcatEnumerable(ITemplateExecutionContext handlerDataContext, IEnumerable enumerable)
        {
            using var builderHolder = handlerDataContext.TemplateServices.StringBuilderPool.Get();
            var builder = builderHolder.Item;

            foreach (var value in enumerable)
            {
                builder.Append(value);
            }

            return new ValueTask<object>(builder.ToString());
        }
    }
}
