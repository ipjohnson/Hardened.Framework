using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl
{
    public class TemplateDefaultOutputFunc
    {
        private readonly IStringBuilderPool _stringBuilderPool;
        private readonly TemplateExecutionFunction _templateExecutionFunction;

        public TemplateDefaultOutputFunc(IStringBuilderPool stringBuilderPool, TemplateExecutionFunction templateExecutionFunction)
        {
            _stringBuilderPool = stringBuilderPool;
            _templateExecutionFunction = templateExecutionFunction;
        }

        public async Task Execute(IExecutionContext context)
        {
            using var stringBuilderHolder = _stringBuilderPool.Get();

            var stringBuilderWriter = new StringBuilderTemplateOutputWriter(stringBuilderHolder.Item, context.Response.Body);

            // todo: update to get content type from template extension 
            context.Response.ContentType = "text/html";

            if (context.Response.ResponseValue != null)
            {
                await _templateExecutionFunction(context.Response.ResponseValue, context.RequestServices,
                    stringBuilderWriter, null, context);

                await stringBuilderWriter.FlushWriter();
            }
        }
    }
}
