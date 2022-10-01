using System.Web;
using Hardened.Templates.Abstract;

namespace Hardened.Templates.Runtime.Impl;

public class HtmlEscapeStringService : IStringEscapeService
{
    public bool CanEscapeTemplate(string templateExtension)
    {
        return templateExtension.EndsWith("html");
    }

    public string EscapeString(string? value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return HttpUtility.HtmlEncode(value);
    }
}