using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request
{
    public class ResponseInformationModel
    {
        public bool IsAsync { get; set; }

        public string? TemplateName { get; set; }

        public ITypeDefinition? ReturnType { get; set; }

    }
}
