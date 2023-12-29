namespace Hardened.Requests.Abstract.Attributes;

/// <summary>
/// Attribute request handlers with this to return template instead of raw data
/// </summary>
public class TemplateAttribute : Attribute {
    public TemplateAttribute(string templateName) {
        TemplateName = templateName;
    }

    public string TemplateName { get; }
}