using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request;

public record ResponseInformationModel
{
    public bool IsAsync { get; set; }

    public ITypeDefinition? ReturnType { get; set; }

    public string? TemplateName { get; set; }

    public int? DefaultStatusCode { get; set; }

    public string? RawResponseContentType { get; set; }
    
    public override string ToString()
    {
        return $"{IsAsync}:{TemplateName}:{ReturnType}";
    }
    
}