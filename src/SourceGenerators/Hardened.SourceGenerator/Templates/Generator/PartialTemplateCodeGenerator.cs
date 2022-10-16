using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Templates.Generator;

public class PartialTemplateCodeGenerator
{
    private readonly TemplateInvokeHelperCodeGenerator _helperCodeGenerator = new ();

    public void ProcessPartialTemplateNode(TemplateImplementationGenerator.GenerationContext context)
    {
        if (context.CurrentNode!.ArgumentList.Count is > 0 and <= 2)
        {
            var partialTemplateFunction = GetOrCreatePartialTemplateFunctionField(
                context, context.CurrentNode.ArgumentList[0].ActionText);

            var argument = context.CurrentModel.Name;

            if (context.CurrentNode.ArgumentList.Count == 2)
            {
                var currentNode = context.CurrentNode;

                context.CurrentNode = currentNode.ArgumentList[1];

                argument = GetTemplateArgument(context);

                context.CurrentNode = currentNode;
            }

            context.CurrentBlock.AddCode("await [arg1]([arg2], serviceProvider, writer, executionContext, requestExecutionContext);", partialTemplateFunction, argument);
        }
    }

    private string GetTemplateArgument(TemplateImplementationGenerator.GenerationContext context)
    {
        if (context.CurrentNode.ActionText.StartsWith("$"))
        {
            return _helperCodeGenerator.AssignNodeToVariable(context);
        }

        if (context.CurrentNode.ActionText.EndsWith("/"))
        {
            return context.CurrentNode.ActionText.Substring(1);
        }

        return context.CurrentModel.Name + "." + context.CurrentNode.ActionText;
    }

    private string GetOrCreatePartialTemplateFunctionField(TemplateImplementationGenerator.GenerationContext context, string actionText)
    {
        var functionFieldName = "_template_" + actionText.Replace('.', '_').Replace('-', '_');

        if (context.ClassDefinition.Fields.All(f => f.Name != functionFieldName))
        {
            context.ClassDefinition.AddField(KnownTypes.Templates.TemplateExecutionFunction, functionFieldName);
            context.InitMethod.AddCode(
                "[arg1] = _templateExecutionService.FindTemplateExecutionFunction({arg2}) ?? throw new Exception(\"Could not locate template {arg2}\");", 
                functionFieldName,
                actionText);
        }

        return functionFieldName;
    }
}