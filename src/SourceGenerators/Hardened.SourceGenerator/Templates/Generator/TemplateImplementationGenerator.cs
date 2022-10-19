using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Parser;

namespace Hardened.SourceGenerator.Templates.Generator;

public class TemplateImplementationGenerator
{
    private readonly TemplateInvokeHelperCodeGenerator _templateHelperCodeGenerator = new ();
    private readonly PartialTemplateCodeGenerator _partialTemplateCodeGenerator = new ();

    public void GenerateImplementation(ClassDefinition classDefinition, string templateExtension, IList<TemplateActionNode> templateNodes)
    {
        var initializeMethodDefinition = InitializeMethodDefinition(classDefinition, templateExtension);
        var executeMethodDefinition = ExecuteMethodDefinition(classDefinition);

        var context = new GenerationContext(
            classDefinition, 
            executeMethodDefinition, 
            initializeMethodDefinition,
            executeMethodDefinition, 
            new InstanceDefinition("model"));

        executeMethodDefinition.AddCode("var executionContext = new {arg1}({arg2}, serviceProvider, requestValue, _services, _templateExecutionService, _stringEscapeService, writer, parentTemplateExecutionContext, requestExecutionContext);", KnownTypes.Templates.TemplateExecutionContext, templateExtension);
        executeMethodDefinition.AddCode("var currentEscapeStringService = writer.EscapeService;");
        executeMethodDefinition.AddCode("writer.EscapeService = _stringEscapeService;");

        executeMethodDefinition.NewLine();

        var startingNode = ProcessModelUsingAndInjectStatements(classDefinition, executeMethodDefinition, templateNodes);
            
        for (var index = startingNode; index < templateNodes.Count; index++)
        {
            context.CurrentNode = templateNodes[index];

            ProcessActionNode(context);
        }

        executeMethodDefinition.NewLine();

        executeMethodDefinition.AddCode("writer.EscapeService = currentEscapeStringService;");

        executeMethodDefinition.NewLine();

        if ((executeMethodDefinition.Modifiers & ComponentModifier.Async) != ComponentModifier.Async)
        {
            executeMethodDefinition.AddCode("return {arg1}.CompletedTask;", typeof(Task));
        }
    }

    private MethodDefinition InitializeMethodDefinition(ClassDefinition classDefinition, string templateExtension)
    {
        var initializeMethodDefinition = classDefinition.AddMethod("Initialize");

        classDefinition.AddField(KnownTypes.Templates.IStringEscapeService, "_stringEscapeService");

        initializeMethodDefinition.AddCode(
            "_stringEscapeService = _services.StringEscapeServiceProvider.GetEscapeService({arg1});", templateExtension);

        return initializeMethodDefinition;
    }

    private static MethodDefinition ExecuteMethodDefinition(ClassDefinition classDefinition)
    {
        var methodDefinition = classDefinition.AddMethod("Execute");

        methodDefinition.Modifiers = ComponentModifier.Public;

        methodDefinition.AddParameter(typeof(object), "requestValue");
        methodDefinition.AddParameter(typeof(IServiceProvider), "serviceProvider");
        methodDefinition.AddParameter(KnownTypes.Templates.ITemplateOutputWriter, "writer");
        methodDefinition.AddParameter(KnownTypes.Templates.ITemplateExecutionContext.MakeNullable(),
            "parentTemplateExecutionContext");
        methodDefinition.AddParameter(KnownTypes.Requests.IExecutionContext.MakeNullable(), "requestExecutionContext");
            
        methodDefinition.SetReturnType(typeof(Task));

        return methodDefinition;
    }

    private int ProcessModelUsingAndInjectStatements(
        ClassDefinition classDefinition, MethodDefinition methodDefinition, IList<TemplateActionNode> templateActionNodes)
    {
        var startIndex = 0;

        if (templateActionNodes.Count > 0)
        {
            if (templateActionNodes[0].ActionText == "model")
            {
                methodDefinition.AddCode(
                    $"var model = ({templateActionNodes[0].ArgumentList.First().ActionText})requestValue;");

                startIndex++;
            }
            
            var processingUsingStatement = true;
                
            while (processingUsingStatement)
            {
                if (templateActionNodes[startIndex].ActionText == "using")
                {
                    var usingNode = templateActionNodes[startIndex];

                    if (usingNode.ArgumentList.Count == 1)
                    {
                        methodDefinition.AddUsingNamespace(usingNode.ArgumentList[0].ActionText);
                    }
                        
                    templateActionNodes.RemoveAt(startIndex);
                }
                else
                {
                    processingUsingStatement = false;
                }
            }

            var processingInjectStatement = true;
            var serviceProvider = methodDefinition.Parameters.First(p => p.Name == "serviceProvider");

            while (processingInjectStatement)
            {
                if (templateActionNodes[startIndex].ActionText == "inject")
                {
                    var injectNode = templateActionNodes[startIndex];
                    
                    if (injectNode.ArgumentList.Count == 2)
                    {
                        var invoke = serviceProvider.InvokeGeneric(
                            "GetRequiredService",
                            new[] { TypeDefinition.Get("", injectNode.ArgumentList[0].ActionText) });

                        methodDefinition.Assign(invoke).ToVar(injectNode.ArgumentList[1].ActionText);
                        methodDefinition.AddUsingNamespace(KnownTypes.Namespace.Microsoft.Extensions.DependencyInjection);
                    }

                    templateActionNodes.RemoveAt(startIndex);
                }
                else
                {
                    processingInjectStatement = false;
                }
            }
        }

        return startIndex;
    }

    private void ProcessActionNode(GenerationContext context)
    {
        if (context.CurrentNode != null)
        {
            switch (context.CurrentNode.Action)
            {
                case TemplateActionType.Block:
                    BlockActionNode(context);
                    break;

                case TemplateActionType.Content:
                    ContentActionNode(context);
                    break;

                case TemplateActionType.RawMustacheToken:
                case TemplateActionType.MustacheToken:
                    MustacheTokenActionNode(context);
                    break;

                case TemplateActionType.StringLiteral:
                    StringLiteralActionNode(context);
                    break;

                default:
                    throw new Exception($"TemplateActionType not supported {context.CurrentNode.Action}");
            }
        }
    }

    private void StringLiteralActionNode(GenerationContext context)
    {
        if (context.CurrentNode == null)
        {
            return;
        }

        context.CurrentNode.FieldName = $"\"{context.CurrentNode.ActionText}\"";
    }

    private void MustacheTokenActionNode(GenerationContext context)
    {
        if (context.CurrentNode == null)
        {
            return;
        }

        var actionNode = context.CurrentNode;

        if (actionNode.ActionText == "model" || 
            actionNode.ActionText == "using" ||
            actionNode.ActionText == "inject")
        {
            return;
        }

        if (actionNode.ActionText.StartsWith("$"))
        {
            context.InvokeMethod.Modifiers |= ComponentModifier.Async;
                
            _templateHelperCodeGenerator.WriteTemplateHelperToBuilder(context);
        }
        else if (actionNode.ActionText == ">")
        {
            context.InvokeMethod.Modifiers |= ComponentModifier.Async;
                
            _partialTemplateCodeGenerator.ProcessPartialTemplateNode(context);
        }
        else
        {
            var formatString = "null";

            if (actionNode.ArgumentList.Count == 2 &&
                actionNode.ArgumentList[0].ActionText == ":" && 
                actionNode.ArgumentList[1].Action == TemplateActionType.StringLiteral)
            {
                formatString = "\"" + actionNode.ArgumentList[1].ActionText + "\"";
            }

            var csharpStatement = GetCsharpStatement(context,  actionNode);

            var writeMethod =
                context.CurrentNode!.Action == TemplateActionType.RawMustacheToken ? "WriteRaw" : "Write";

            context.CurrentBlock.AddCode(
                $"writer.{writeMethod}({TemplateSource.FormatDataCall}(executionContext, \"{actionNode.ActionText}\", {csharpStatement}, {formatString}));");
        }
    }

    private static string GetCsharpStatement(GenerationContext context, TemplateActionNode actionNode)
    {
        var csharpStatement = actionNode.ActionText;

        if (csharpStatement.StartsWith("^"))
        {
            csharpStatement = csharpStatement.Substring(1);

            foreach (TemplateActionNode argumentNode in actionNode.ArgumentList)
            {
                var nodeText = argumentNode.ActionText;

                if (argumentNode.Action == TemplateActionType.StringLiteral)
                {
                    nodeText = '"' + nodeText + '"';
                }
                csharpStatement += " " + nodeText;
            }
        }
        else if (csharpStatement.StartsWith("`"))
        {
            context.InvokeMethod.AddUsingNamespace(KnownTypes.Namespace.Hardened.Templates.Runtime.Value);

            var propertyStatement = csharpStatement.Substring(1);
                
            csharpStatement = $"new PropertyValue({context.CurrentModel.Name}.{propertyStatement}, \"{propertyStatement}\")";
        }
        else
        {
            csharpStatement = context.CurrentModel.Name + "." + csharpStatement;
        }

        return csharpStatement;
    }

    private void ContentActionNode(GenerationContext context)
    {
        if (context.CurrentNode == null)
        {
            return;
        }

        var fieldName = context.CurrentNode.FieldName;

        if (!string.IsNullOrEmpty(context.CurrentNode.FieldName))
        {
            context.CurrentBlock.AddCode($"writer.WriteRaw({fieldName});");
        }
    }

    private void BlockActionNode(GenerationContext context)
    {
        if (context.CurrentNode == null)
        {
            return;
        }

        context.CurrentBlock.NewLine();
            
        switch (context.CurrentNode.ActionText)
        {
            case "each":
                EachBlockNode(context);
                break;
            case "if":
                IfBlockNode(context);
                break;
        }

        context.CurrentBlock.NewLine();
    }

    private void IfBlockNode(GenerationContext context)
    {
        if (context.CurrentNode == null)
        {
            return;
        }

        var arguments = ProcessArgumentList(context.CurrentNode.ArgumentList, context.CurrentModel);

        var ifBlock = context.CurrentBlock.If($"_services.BooleanLogicService.IsTrueValue({arguments})");
            
        context.PushBlock(ifBlock, context.CurrentModel);

        foreach (var childNode in context.CurrentNode.ChildNodes)
        {
            if (childNode.ActionText == "else")
            {
                context.PopBlock();

                if (childNode.ArgumentList.Count > 1)
                {
                    if (childNode.ArgumentList[0].ActionText == "if")
                    {
                        var newList = new List<TemplateActionNode>(childNode.ArgumentList);

                        newList.RemoveAt(0);

                        arguments = ProcessArgumentList(newList, context.CurrentModel);

                        var elseIf = ifBlock.ElseIf($"_services.BooleanLogicService.IsTrueValue({arguments})");

                        context.PushBlock(elseIf, context.CurrentModel);
                    }
                }
                else
                {
                    var elseBlock = ifBlock.Else();
                    context.PushBlock(elseBlock, context.CurrentModel);
                }
            }
            else
            {
                context.CurrentNode = childNode;

                ProcessActionNode(context);
            }
        }

        context.PopBlock();
    }

    private void EachBlockNode(GenerationContext context)
    {
        if (context.CurrentNode == null)
        {
            return;
        }

        if (context.CurrentNode.ArgumentList.Count != 1)
        {
            throw new Exception("each block doesn't support multiple arguments");
        }

        var csharpStatement = GetCsharpStatement(context, context.CurrentNode.ArgumentList.First());

        var eachVariable = context.InvokeMethod.GetUniqueVariable("eachVariable");
            
        var foreachBlock = 
            context.CurrentBlock.ForEach(eachVariable, CodeOutputComponent.Get(csharpStatement));

        context.PushBlock(foreachBlock, foreachBlock.Instance);
            
        ProcessBlockChildNodes(context, context.CurrentNode.ChildNodes);

        context.PopBlock();
    }

    private void ProcessBlockChildNodes(
        GenerationContext context, IList<TemplateActionNode> actionNodes)
    {
        foreach (var actionNode in actionNodes)
        {
            context.CurrentNode = actionNode;

            ProcessActionNode(context);
        }
    }

    private string ProcessArgumentList(IEnumerable<TemplateActionNode> nodes, InstanceDefinition contextVariable)
    {
        string returnString = "";

        foreach (var actionNode in nodes)
        {
            if (string.IsNullOrEmpty(actionNode.ActionText))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(returnString))
            {
                returnString += " ";
            }

            var firstChar = actionNode.ActionText[0];

            if (char.IsLetter(firstChar) || firstChar == '_')
            {
                returnString += contextVariable.Name + "." + actionNode.ActionText;
            }
            else if (firstChar == '`')
            {
                returnString += actionNode.ActionText.TrimStart('`');
            }
            else
            {
                returnString += actionNode.ActionText.TrimStart('^');
            }
        }

        return returnString;
    }

    public class GenerationContext
    {
        private readonly Stack<Tuple<BaseBlockDefinition, InstanceDefinition>> _currentModel = new();
            
        public GenerationContext(ClassDefinition classDefinition,
            MethodDefinition invokeMethod,
            MethodDefinition initMethod, 
            BaseBlockDefinition currentBlock,
            InstanceDefinition instance)
        {
            ClassDefinition = classDefinition;
            InvokeMethod = invokeMethod;
            InitMethod = initMethod;

            _currentModel.Push(new Tuple<BaseBlockDefinition, InstanceDefinition>(currentBlock, instance));
        }

        public ClassDefinition ClassDefinition { get; }

        public MethodDefinition InvokeMethod { get; }

        public MethodDefinition InitMethod { get; }

        public BaseBlockDefinition CurrentBlock => _currentModel.Peek().Item1;

        public TemplateActionNode? CurrentNode { get; set; }

        public InstanceDefinition CurrentModel => _currentModel.Peek().Item2;

        public void PushBlock(BaseBlockDefinition currentBlock,InstanceDefinition instanceDefinition) => 
            _currentModel.Push(new Tuple<BaseBlockDefinition, InstanceDefinition>(currentBlock, instanceDefinition));

        public void PopBlock() => _currentModel.Pop();
    }
}