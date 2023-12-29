namespace Hardened.Templates.Abstract;

public interface ITemplateHelperProvider {
    TemplateHelperFactory? GetTemplateHelperFactory(string mustacheToken);
}