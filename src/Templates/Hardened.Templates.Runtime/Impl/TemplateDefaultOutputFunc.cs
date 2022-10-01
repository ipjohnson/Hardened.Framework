using Hardened.Requests.Abstract.Execution;
using Hardened.Shared.Runtime.Collections;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public class TemplateDefaultOutputFunc
{
    private readonly IStringBuilderPool _stringBuilderPool;
    private readonly TemplateExecutionFunction _templateExecutionFunction;
    private readonly TemplateExecutionFunction? _layoutFunction;

    public TemplateDefaultOutputFunc(IStringBuilderPool stringBuilderPool,
        TemplateExecutionFunction templateExecutionFunction, 
        TemplateExecutionFunction? layoutFunction)
    {
        _stringBuilderPool = stringBuilderPool;
        _templateExecutionFunction = templateExecutionFunction;
        _layoutFunction = layoutFunction;
    }

    public async Task Execute(IExecutionContext context)
    {
        using var stringBuilderHolder = _stringBuilderPool.Get();

        var stringBuilderWriter = new StringBuilderTemplateOutputWriter(stringBuilderHolder.Item, context.Response.Body);

        // todo: update to get content type from template extension 
        context.Response.ContentType = "text/html";

        if (context.Response.ResponseValue != null)
        {
            if (_layoutFunction == null || 
                (context.Request.Headers.TryGet("x-render-partial", out var renderStringValues)  && renderStringValues.Contains("true"))) 
            {
                await _templateExecutionFunction(context.Response.ResponseValue, context.RequestServices,
                    stringBuilderWriter, null, context);
            }
            else
            {
                await _layoutFunction(
                    new LayoutModel(context.Response.ResponseValue, _templateExecutionFunction), 
                    context.RequestServices,
                    stringBuilderWriter, 
                    null,
                    context);
            }
                
            await stringBuilderWriter.FlushWriter();
        }
    }
}