using Hardened.Templates.Abstract;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;

namespace Hardened.Templates.Runtime.Impl;
public class NoopEscapeStringService : IStringEscapeService
{
    public bool CanEscapeTemplate(string templateExtension)
    {
        return templateExtension.EndsWith("css") || 
               templateExtension.EndsWith("js") || 
               templateExtension.EndsWith("md");
    }

    public string EscapeString(string? value)
    {
        return value ?? "";
    }
}