namespace Hardened.Templates.Abstract
{
    public delegate ITemplateHelper TemplateHelperFactory(IServiceProvider serviceProvider);

    public interface ITemplateHelperService
    {
        TemplateHelperFactory LocateHelper(string helperToken);
    }
}
