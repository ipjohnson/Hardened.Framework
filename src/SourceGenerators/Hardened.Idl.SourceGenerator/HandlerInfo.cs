using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;

namespace Hardened.Idl.SourceGenerator;

internal class HandlerInfo : IEquatable<HandlerInfo> {
    public HandlerInfo(
        ITypeDefinition implementationType,
        IReadOnlyList<ITypeDefinition> interfaceCandidates,
        IReadOnlyList<AttributeModel> classFilters,
        IReadOnlyList<HandlerMethodFilterInfo> methodFilters,
        Location? location = null) {
        ImplementationType = implementationType;
        InterfaceCandidates = interfaceCandidates;
        ClassFilters = classFilters;
        MethodFilters = methodFilters;
        Location = location;
    }

    /// <summary>
    /// A handler whose base list is one interface, which is the ordinary shape.
    /// </summary>
    public HandlerInfo(
        ITypeDefinition implementationType,
        ITypeDefinition interfaceType,
        IReadOnlyList<AttributeModel> classFilters,
        IReadOnlyList<HandlerMethodFilterInfo> methodFilters,
        Location? location = null)
        : this(implementationType, new[] { interfaceType }, classFilters, methodFilters, location) { }

    public ITypeDefinition ImplementationType { get; }

    /// <summary>
    /// Every entry in the class's base list, in the order it was written.
    /// </summary>
    /// <remarks>
    /// Which one is the service interface is not decidable where this is built - the interface is
    /// generated, and the semantic model of that pass may not carry it yet - so the choice is
    /// deferred to whoever knows what the description declared. See
    /// <see cref="ServiceInterface"/>.
    /// </remarks>
    public IReadOnlyList<ITypeDefinition> InterfaceCandidates { get; }

    /// <summary>
    /// The first base-list entry, which is what this used to assume was the service interface.
    /// </summary>
    /// <remarks>
    /// Kept as the fallback for a handler whose base list matches no declared service, so a
    /// registration that worked before still works. It is not the answer - <c>ServiceInterface</c>
    /// is - and it is wrong precisely when a handler has a base class, because C# requires that to
    /// come first.
    /// </remarks>
    public ITypeDefinition InterfaceType => InterfaceCandidates[0];

    public IReadOnlyList<AttributeModel> ClassFilters { get; }

    public IReadOnlyList<HandlerMethodFilterInfo> MethodFilters { get; }

    /// <summary>Where the handler is declared, for a diagnostic that has to point at it.</summary>
    public Location? Location { get; }

    /// <summary>
    /// The base-list entry naming one of <paramref name="declaredServiceNames"/>, or null.
    /// </summary>
    /// <remarks>
    /// Matched on the simple name, which is what the routing table and the model builder already
    /// compare - the generated interface's namespace is the emitting project's and a handler may
    /// spell it unqualified.
    /// </remarks>
    public ITypeDefinition? ServiceInterface(ICollection<string> declaredServiceNames) {
        foreach (var candidate in InterfaceCandidates) {
            if (declaredServiceNames.Contains(candidate.Name)) {
                return candidate;
            }
        }

        return null;
    }

    public bool Equals(HandlerInfo? other) {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (!ImplementationType.Equals(other.ImplementationType)) return false;
        if (InterfaceCandidates.Count != other.InterfaceCandidates.Count) return false;

        for (var i = 0; i < InterfaceCandidates.Count; i++) {
            if (!InterfaceCandidates[i].Equals(other.InterfaceCandidates[i])) return false;
        }

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
            hash = (hash * 397) ^ InterfaceCandidates.GetHashCodeAggregation();
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
