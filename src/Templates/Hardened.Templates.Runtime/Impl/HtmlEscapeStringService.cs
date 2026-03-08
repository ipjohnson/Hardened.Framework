using System.Web;
using DependencyModules.Runtime.Attributes;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

[SingletonService]
public class HtmlEscapeStringService : IStringEscapeService {
    public bool CanEscapeTemplate(string templateExtension) {
        return templateExtension.EndsWith("html");
    }

    public string EscapeString(string? value) {
        if (value == null) {
            return string.Empty;
        }

        return HttpUtility.HtmlEncode(value);
    }
}