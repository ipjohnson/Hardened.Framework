using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Idl.SourceGenerator;

internal class HandlerInfo : IEquatable<HandlerInfo> {
    public HandlerInfo(
        ITypeDefinition implementationType,
        ITypeDefinition interfaceType,
        IReadOnlyList<AttributeModel> classFilters,
        IReadOnlyList<HandlerMethodFilterInfo> methodFilters) {
        ImplementationType = implementationType;
        InterfaceType = interfaceType;
        ClassFilters = classFilters;
        MethodFilters = methodFilters;
    }

    public ITypeDefinition ImplementationType { get; }

    public ITypeDefinition InterfaceType { get; }

    public IReadOnlyList<AttributeModel> ClassFilters { get; }

    public IReadOnlyList<HandlerMethodFilterInfo> MethodFilters { get; }

    public bool Equals(HandlerInfo? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!ImplementationType.Equals(other.ImplementationType)) return false;
        if (!InterfaceType.Equals(other.InterfaceType)) return false;
        if (!ClassFilters.DeepEquals(other.ClassFilters)) return false;
        if (MethodFilters.Count != other.MethodFilters.Count) return false;

        for (var i = 0; i < MethodFilters.Count; i++) {
            if (!MethodFilters[i].Equals(other.MethodFilters[i])) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => Equals(obj as HandlerInfo);

    public override int GetHashCode() {
        unchecked {
            var hash = ImplementationType.GetHashCode();
            hash = (hash * 397) ^ InterfaceType.GetHashCode();
            hash = (hash * 397) ^ ClassFilters.GetHashCodeAggregation();
            hash = (hash * 397) ^ MethodFilters.GetHashCodeAggregation();
            return hash;
        }
    }
}

internal class HandlerMethodFilterInfo : IEquatable<HandlerMethodFilterInfo> {
    public HandlerMethodFilterInfo(
        string methodName,
        IReadOnlyList<AttributeModel> filters,
        ITypeDefinition? outputType = null) {
        MethodName = methodName;
        Filters = filters;
        OutputType = outputType;
    }

    public string MethodName { get; }

    public IReadOnlyList<AttributeModel> Filters { get; }

    /// <summary>
    /// What writes this operation's response, named by <c>[Output&lt;T&gt;]</c>, or null.
    /// </summary>
    /// <remarks>
    /// Carried separately from the filter list because a generic attribute's type argument is what
    /// is wanted here, and an <c>AttributeModel</c> is the attribute as it will be re-emitted. This
    /// is the specification-first side of the same reading the web generator does directly on the
    /// method - a document generates the handler's signature, so the implementation is the only
    /// place a view can be named.
    /// </remarks>
    public ITypeDefinition? OutputType { get; }

    public bool Equals(HandlerMethodFilterInfo? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return MethodName == other.MethodName &&
               Equals(OutputType, other.OutputType) &&
               Filters.DeepEquals(other.Filters);
    }

    public override bool Equals(object? obj) => Equals(obj as HandlerMethodFilterInfo);

    public override int GetHashCode() {
        unchecked {
            var hash = MethodName.GetHashCode();

            hash = (hash * 397) ^ Filters.GetHashCodeAggregation();
            hash = (hash * 397) ^ (OutputType?.GetHashCode() ?? 0);

            return hash;
        }
    }
}
