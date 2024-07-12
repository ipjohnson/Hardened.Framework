namespace Hardened.SourceGenerator.Templates.Parser;

public class TemplateWhiteSpaceCleaner {
    private static readonly char[] _TildeChars = new char[] { '\r', '\n', '\t', ' ' };

    public void RemoveWhitespace(IList<TemplateActionNode> actionNodes) {
        if (actionNodes is { Count: > 1 }) {
            RemoveModelWhiteSpace(actionNodes);

            RemoveUsingWhiteSpace(actionNodes);

            RemoveTildeWhiteSpace(actionNodes);
        }
    }

    private void RemoveModelWhiteSpace(IList<TemplateActionNode> actionNodes) {
        var firstNode = actionNodes[1];

        if (firstNode.Action == TemplateActionType.Content) {
            firstNode.ActionText = firstNode.ActionText?.TrimStart('\r', '\n') ?? "";
        }
    }

    private void RemoveUsingWhiteSpace(IList<TemplateActionNode> actionNodes) {
        var currentIndex = 2;

        for (; currentIndex < actionNodes.Count;) {
            var firstStatement = actionNodes[currentIndex - 1];
            var secondStatement = actionNodes[currentIndex];

            if (firstStatement.Action == TemplateActionType.Content &&
                secondStatement.Action == TemplateActionType.MustacheToken &&
                (secondStatement.ActionText == "using" || secondStatement.ActionText == "inject")) {
                actionNodes.RemoveAt(currentIndex - 1);

                if (actionNodes.Count > currentIndex) {
                    var nextStatement = actionNodes[currentIndex];

                    if (nextStatement.Action == TemplateActionType.Content) {
                        nextStatement.ActionText = nextStatement.ActionText?.TrimStart('\r', '\n') ?? "";
                    }
                }

                currentIndex++;
            }
            else {
                break;
            }
        }
    }

    private void RemoveTildeWhiteSpace(IList<TemplateActionNode> actionNodes) {
        for (var index = 0; index < actionNodes.Count; index++) {
            var actionNode = actionNodes[index];

            foreach (var trimAttribute in actionNode.TrimAttributes) {
                switch (trimAttribute) {
                    case TemplateActionNodeTrimAttribute.OpenStart:
                        RemoveOpenStartWhitespace(actionNodes, index);
                        break;
                    case TemplateActionNodeTrimAttribute.OpenEnd:
                        RemoveOpenEndWhitespace(actionNodes, index);
                        break;
                    case TemplateActionNodeTrimAttribute.CloseStart:
                        RemoveCloseStartWhitespace(actionNodes, index);
                        break;
                    case TemplateActionNodeTrimAttribute.CloseEnd:
                        RemoveCloseEndWhitespace(actionNodes, index);
                        break;
                }
            }

            RemoveTildeWhiteSpace(actionNode.ChildNodes);
        }
    }

    private void RemoveCloseEndWhitespace(IList<TemplateActionNode> actionNodes, int index) {
        index++;

        if (index < actionNodes.Count) {
            var node = actionNodes[index];

            node.ActionText = node.ActionText.TrimStart(_TildeChars);
        }
    }

    private void RemoveCloseStartWhitespace(IList<TemplateActionNode> actionNodes, int index) {
        var node = actionNodes[index];

        var argument = node.ChildNodes.LastOrDefault();

        if (argument is { Action: TemplateActionType.Content }) {
            argument.ActionText = argument.ActionText.TrimEnd(_TildeChars);
        }
    }

    private void RemoveOpenEndWhitespace(IList<TemplateActionNode> actionNodes, int index) {
        var node = actionNodes[index];

        var argument = node.ChildNodes.FirstOrDefault();

        if (argument is { Action: TemplateActionType.Content }) {
            argument.ActionText = argument.ActionText.TrimStart(_TildeChars);
        }
    }

    private void RemoveOpenStartWhitespace(IList<TemplateActionNode> actionNodes, int index) {
        if (--index < 0) {
            return;
        }

        var node = actionNodes[index];

        if (node is { Action: TemplateActionType.Content }) {
            node.ActionText = node.ActionText.TrimEnd(_TildeChars);
        }
    }
}