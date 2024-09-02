using System.Text;
using CSharpAuthor;

namespace Hardened.SourceGenerator.Shared;

public class HardenedMethodDefinition {
    public HardenedMethodDefinition(string name, ITypeDefinition? returnType,
        IReadOnlyList<HardenedParameterDefinition> parameters) {
        Name = name;
        ReturnType = returnType;
        Parameters = parameters;
    }

    public string Name { get; }

    public ITypeDefinition? ReturnType { get; }

    public IReadOnlyList<HardenedParameterDefinition> Parameters { get; }

    public override bool Equals(object obj) {
        if (obj is not HardenedMethodDefinition other) {
            return false;
        }

        if (!Name.Equals(other.Name)) {
            return false;
        }

        if (!(ReturnType?.Equals(other.ReturnType) ?? true)) {
            return false;
        }

        if (Parameters.Count != other.Parameters.Count) {
            return false;
        }

        for (var i = 0; i < Parameters.Count; i++) {
            var xP = Parameters[i];
            var yP = other.Parameters[i];

            if (!xP.Equals(yP)) {
                return false;
            }
        }

        return true;
    }

    public override string ToString() {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append(ReturnType);
        stringBuilder.Append(" ");
        stringBuilder.Append(Name);
        stringBuilder.Append("(");
        var comma = false;
        foreach (var parameter in Parameters) {
            if (comma) {
                stringBuilder.Append(',');
            }
            else {
                comma = true;
            }

            stringBuilder.Append(parameter);
        }

        stringBuilder.Append(")");

        return stringBuilder.ToString();
    }

    public override int GetHashCode() {
        return ToString().GetHashCode();
    }
}

public class HardenedParameterDefinition {
    public HardenedParameterDefinition(string name, ITypeDefinition type) {
        Name = name;
        Type = type;
    }

    public string Name { get; }

    public ITypeDefinition Type { get; }

    public override bool Equals(object obj) {
        if (obj is not HardenedParameterDefinition hardenedParameterDefinition) {
            return false;
        }

        if (hardenedParameterDefinition == null) {
            throw new ArgumentNullException(nameof(hardenedParameterDefinition));
        }
        
        if (hardenedParameterDefinition.Name != Name) {
            return false;
        }

        if (hardenedParameterDefinition.Type == null) {
            throw new ArgumentNullException(nameof(hardenedParameterDefinition.Type));
        }

        if (!hardenedParameterDefinition.Type.Equals(Type)) {
            return false;
        }

        return true;
    }

    public override string ToString() {
        return $"{Type} {Name}";
    }

    public override int GetHashCode() {
        return ToString().GetHashCode();
    }
}