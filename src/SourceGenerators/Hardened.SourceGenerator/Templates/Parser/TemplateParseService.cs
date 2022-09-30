using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Templates.Parser
{
    public class TemplateParseService
    {
        private readonly StringTokenNodeParser _stringTokenNodeParser;

        public TemplateParseService(StringTokenNodeParser stringTokenNodeParser)
        {
            _stringTokenNodeParser = stringTokenNodeParser;
        }

        public IList<TemplateActionNode> ParseTemplate(string templateString, StringTokenNodeParser.TokenInfo tokenInfo)
        {
            var tokens = _stringTokenNodeParser.ParseTemplate(templateString, tokenInfo);
            
            var templateActionNodes = CreateTemplateActionNodesFromTokens(tokens);

            return templateActionNodes;
        }

        private IList<TemplateActionNode> CreateTemplateActionNodesFromTokens(IList<StringTokenNode> tokens)
        {
            var actionNodeList = new List<TemplateActionNode>();
            var currentActionNodeStack = new Stack<TemplateActionNode>();

            foreach (var tokenNode in tokens)
            {
                if (tokenNode.TokenNodeType == StringTokenNodeType.Content)
                {
                    var actionNode = NewContentTemplateActionNode(tokenNode);

                    if (currentActionNodeStack.Count == 0)
                    {
                        actionNodeList.Add(actionNode);
                    }
                    else
                    {
                        currentActionNodeStack.Peek().ChildNodes.Add(actionNode);
                    }
                }
                else
                {
                    var token = tokenNode.Token;

                    var trimStart = token.StartsWith('~');
                    var trimEnd = token.EndsWith("~".ToCharArray());

                    if (trimStart)
                    {
                        token = token.Slice(1);
                    }

                    if (trimEnd)
                    {
                        token = token.Slice(0, token.Length - 1);
                    }

                    if (IsBlockOpenNode(token))
                    {
                        var actionToken = GetActionToken(token.Slice(1));

                        var argumentStartIndex = actionToken.Length + 1;
                        var argumentLength = token.Length - argumentStartIndex;

                        ProcessTokenArguments(
                            token.Slice(
                            argumentStartIndex,
                            argumentLength),
                            out var actionTokenArguments);

                        var templateActionNode = new TemplateActionNode(
                            TemplateActionType.Block,
                            actionToken.ToString(),
                            actionTokenArguments,
                            new List<TemplateActionNode>(),
                            new List<TemplateActionNodeTrimAttribute>()
                        );

                        if (trimStart)
                        {
                            templateActionNode.TrimAttributes.Add(TemplateActionNodeTrimAttribute.OpenStart);
                        }

                        if (trimEnd)
                        {
                            templateActionNode.TrimAttributes.Add(TemplateActionNodeTrimAttribute.OpenEnd);
                        }

                        currentActionNodeStack.Push(templateActionNode);
                    }
                    else if (IsBlockCloseNode(token))
                    {
                        var actionToken = GetActionToken(token.Slice(1));

                        if (currentActionNodeStack.Count == 0)
                        {
                            throw new Exception($"No open tag for {actionToken.ToString()}");
                        }

                        var currentAction = currentActionNodeStack.Pop();

                        if (!currentAction.ActionText.Equals(actionToken.ToString()))
                        {
                            throw new Exception(
                                $"Template mismatch, expected '{currentAction.ActionText}' but found '{actionToken.ToString()}'");
                        }

                        if (trimStart)
                        {
                            currentAction.TrimAttributes.Add(TemplateActionNodeTrimAttribute.CloseStart);
                        }

                        if (trimEnd)
                        {
                            currentAction.TrimAttributes.Add(TemplateActionNodeTrimAttribute.CloseEnd);
                        }

                        if (currentActionNodeStack.Count == 0)
                        {
                            actionNodeList.Add(currentAction);
                        }
                        else
                        {
                            currentActionNodeStack.Peek().ChildNodes.Add(currentAction);
                        }
                    }
                    else
                    {
                        var actionNode = ProcessActionToken(tokenNode, token, trimStart, trimEnd);

                        if (trimStart)
                        {
                            actionNode?.TrimAttributes.Add(TemplateActionNodeTrimAttribute.OpenStart);
                        }

                        if (trimEnd)
                        {
                            actionNode?.TrimAttributes.Add(TemplateActionNodeTrimAttribute.CloseEnd);
                        }

                        if (actionNode != null)
                        {
                            if (currentActionNodeStack.Count == 0)
                            {
                                actionNodeList.Add(actionNode);
                            }
                            else
                            {
                                currentActionNodeStack.Peek().ChildNodes.Add(actionNode);
                            }
                        }
                    }
                }
            }

            return actionNodeList;
        }

        private TemplateActionNode? ProcessActionToken(
            StringTokenNode tokenNode,
            ReadOnlySpan<char> token,
            bool trimStart, 
            bool trimEnd)
        {
            TemplateActionNode? currentActionNode = null;
            var currentIndex = 0;

            while (currentIndex < token.Length)
            {
                currentIndex = TrimSpaceFromFront(token, currentIndex);

                var actionToken = GetActionToken(token.Slice(currentIndex));

                currentIndex += actionToken.Length;

                currentIndex += ProcessTokenArguments(
                    token.Slice(
                        currentIndex,
                        token.Length - currentIndex),
                    out var actionTokenArguments);

                var actionNode = new TemplateActionNode(
                    TemplateActionType.MustacheToken,
                    actionToken.ToString(),
                    actionTokenArguments,
                    new List<TemplateActionNode>(),
                    new List<TemplateActionNodeTrimAttribute>()
                );

                if (currentActionNode != null)
                {
                    actionTokenArguments.Insert(0, currentActionNode);

                    currentActionNode = actionNode;
                }
                else
                {
                    currentActionNode = actionNode;
                }
            }

            return currentActionNode;
        }

        private int TrimSpaceFromFront(ReadOnlySpan<char> tokenNode, int currentIndex)
        {
            for (; currentIndex < tokenNode.Length && tokenNode[currentIndex] == ' '; currentIndex++)
            {

            }

            return currentIndex;
        }

        private int ProcessTokenArguments(
            ReadOnlySpan<char> token,
            out IList<TemplateActionNode> argumentList)
        {
            argumentList = new List<TemplateActionNode>();

            var currentIndex = 0;

            for (; currentIndex < token.Length;)
            {
                var currentChar = token[currentIndex];

                TemplateActionNode? actionNode;
                switch (currentChar)
                {
                    case ' ':
                        currentIndex++;
                        break;
                    case '(':
                        var subToken = token.Slice(currentIndex + 1, token.Length - (currentIndex + 1));
                        
                        currentIndex += ProcessSubExpression(subToken, out actionNode);

                        argumentList.Add(actionNode);
                        break;
                    case ')':
                        currentIndex++;

                        return currentIndex;
                    case '|':
                        return currentIndex + 1;

                    default:
                        currentIndex += ProcessArgument(token.Slice(currentIndex), out actionNode);

                        if (actionNode != null)
                        {
                            argumentList.Add(actionNode);
                        }
                        break;
                }
            }

            return currentIndex;
        }

        private int ProcessArgument(
            ReadOnlySpan<char> token,
            out TemplateActionNode? templateActionNode)
        {
            templateActionNode = null;
            var argumentCharacter = token[0];

            if (argumentCharacter == '"')
            {
                var endTokenIndex = token.IndexOf('"', 1);

                if (endTokenIndex == -1)
                {
                    throw new Exception($"Could not find end \" in token {token.ToString()}");
                }

                templateActionNode = new TemplateActionNode(
                        TemplateActionType.StringLiteral,
                        token.Slice(1, endTokenIndex - 1).ToString(),
                        new List<TemplateActionNode>(),
                        new List<TemplateActionNode>(),
                        new List<TemplateActionNodeTrimAttribute>()
                    );

                return endTokenIndex + 1;
            }

            var endIndex = token.IndexOf(')');

            if (endIndex == -1)
            {
                endIndex = token.IndexOf(' ');

                if (endIndex == -1)
                {
                    endIndex = token.Length;
                }
            }

            templateActionNode = new TemplateActionNode(
                TemplateActionType.MustacheToken,
                token.Slice(0, endIndex).ToString(),
                new List<TemplateActionNode>(),
                new List<TemplateActionNode>(),
                new List<TemplateActionNodeTrimAttribute>()
            );

            return endIndex;
        }
        
        private int ProcessSubExpression(
            ReadOnlySpan<char> token,
            out TemplateActionNode actionNode)
        {
            var actionToken = GetActionToken(token);

            var currentIndex = actionToken.Length;

            currentIndex += ProcessTokenArguments(
                token.Slice(
                    actionToken.Length,
                    token.Length - currentIndex),
                out var actionTokenArguments);

            actionNode = new TemplateActionNode(
                TemplateActionType.MustacheToken,
                actionToken.ToString(),
                actionTokenArguments,
                new List<TemplateActionNode>(),
                new List<TemplateActionNodeTrimAttribute>()
            );

            return currentIndex + 1;
        }

        private bool IsBlockCloseNode(ReadOnlySpan<char> token)
        {
            return token.StartsWith('/');
        }

        private bool IsBlockOpenNode(ReadOnlySpan<char> token)
        {
            return token.StartsWith('#');
        }

        private TemplateActionNode NewContentTemplateActionNode(StringTokenNode tokenNode)
        {
            return new TemplateActionNode(
                TemplateActionType.Content,
                tokenNode.Token.ToString(),
                new List<TemplateActionNode>(),
                new List<TemplateActionNode>(),
                new List<TemplateActionNodeTrimAttribute>()
                );
        }

        private ReadOnlySpan<char> GetActionToken(ReadOnlySpan<char> token)
        {
            var spaceIndex = token.IndexOf(' ');

            if (spaceIndex == -1)
            {
                return token;
            }

            return token.Slice(0, spaceIndex);
        }
    }
}
