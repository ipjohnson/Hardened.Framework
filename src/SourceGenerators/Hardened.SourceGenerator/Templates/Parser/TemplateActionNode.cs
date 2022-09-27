namespace Hardened.SourceGenerator.Templates.Parser
{
    public enum TemplateActionType
    {
        Content,
        Block,
        MustacheToken,
        StringLiteral
    }

    public class TemplateActionNode
    {
        private static readonly IList<TemplateActionNodeTrimAttribute> _emptyTrimAttributes = new List<TemplateActionNodeTrimAttribute>(0);
        private static readonly IList<TemplateActionNode> _emptyList = new List<TemplateActionNode>(0);

        public TemplateActionNode(
            TemplateActionType action,
            string actionText) 
            : this(action, actionText, _emptyList, _emptyList, _emptyTrimAttributes)
        {

        }
        
        public TemplateActionNode(
            TemplateActionType action,
            string actionText, 
            IList<TemplateActionNode> argumentList)
            : this(action, actionText, argumentList, _emptyList, _emptyTrimAttributes)
        {

        }

        public TemplateActionNode(
            TemplateActionType action, 
            string actionText,
            IList<TemplateActionNode> argumentList,
            IList<TemplateActionNode> childNodes,
            IList<TemplateActionNodeTrimAttribute> trimAttributes)
        {
            Action = action;
            ActionText = actionText;
            ChildNodes = childNodes;
            ArgumentList = argumentList;
            TrimAttributes = trimAttributes;
        }

        public TemplateActionType Action { get; }

        public string ActionText { get; set; }

        public IList<TemplateActionNode> ArgumentList { get; }

        public IList<TemplateActionNode> ChildNodes { get; }

        public IList<TemplateActionNodeTrimAttribute> TrimAttributes { get; }

        public string FieldName { get; set; }
    }
}
