using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Helpers.String
{
    public abstract class BaseStringHelper : ITemplateHelper
    {
        public ValueTask<object> Execute(ITemplateExecutionContext handlerDataContext, params object[] arguments)
        {
            if (arguments is { Length: > 0 } && arguments[0] != null)
            {
                var returnValue = AugmentString(arguments[0].ToString());

                return new ValueTask<object>(returnValue);
            }

            return new ValueTask<object>(null);
        }

        protected abstract object AugmentString(string value);
    }
}
