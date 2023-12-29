namespace Hardened.Templates.Abstract;

public class TemplatePackageAttribute : Attribute {
    public string Extensions { get; set; } = "html";

    public string Token { get; set; } = "{{TOKEN}}";
}