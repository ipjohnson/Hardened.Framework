using CSharpAuthor;

namespace Hardened.SourceGenerator.Models.Request;

public class ResponseInformationModel
{
    public ResponseInformationModel(bool isAsync, string? templateName, ITypeDefinition? returnType)
    {
        IsAsync = isAsync;
        TemplateName = templateName;
        ReturnType = returnType;
    }

    public bool IsAsync { get; }

    public string? TemplateName { get;  }

    public ITypeDefinition? ReturnType { get;  }

    public override bool Equals(object obj)
    {
        if (obj is not ResponseInformationModel responseInformationModel)
        {
            return false;
        }

        if (IsAsync != responseInformationModel.IsAsync)
        {
            return false;
        }

        if (TemplateName != responseInformationModel.TemplateName)
        {
            return false;
        }

        if (!(ReturnType?.Equals(responseInformationModel.ReturnType) ?? true))
        {
            return false;
        }

        return true;
    }

    public override string ToString()
    {
        return $"{IsAsync}:{TemplateName}:{ReturnType}";
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return (IsAsync.GetHashCode() * 397)
                   ^ ((ReturnType?.GetHashCode() ?? 1) * 397)
                   ^ ((TemplateName?.GetHashCode() ?? 1) * 397);
        }
    }
}