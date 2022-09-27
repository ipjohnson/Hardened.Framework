namespace Hardened.Templates.Abstract
{
    public class LayoutModel
    {
        public LayoutModel(object model, TemplateExecutionFunction templateFunc)
        {
            Model = model;
            TemplateFunc = templateFunc;
        }

        public object Model { get; }

        public TemplateExecutionFunction TemplateFunc { get; }

    }
}
