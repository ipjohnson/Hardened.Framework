using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Web.Lambda;

/// <summary>
/// One <c>routes.Get(path, (int id) =&gt; ...)</c> call site, read at build time.
/// </summary>
/// <remarks>
/// Compared by value, so the model keys the incremental cache: adding a registration rebuilds what
/// it needs to and editing an unrelated method does not.
/// </remarks>
public class LambdaRouteModel : IEquatable<LambdaRouteModel>
{
    public LambdaRouteModel(
        string method,
        string interceptsAttribute,
        ITypeDefinition delegateType,
        RequestHandlerModel handler,
        IReadOnlyList<string> boundTokens,
        bool namesVerb
    )
    {
        NamesVerb = namesVerb;
        Method = method;
        InterceptsAttribute = interceptsAttribute;
        DelegateType = delegateType;
        Handler = handler;
        BoundTokens = boundTokens;
    }

    public string Method { get; }

    /// <summary>
    /// Whether the call named its verb in an argument, which <c>Map</c> does and the five verb
    /// methods do not.
    /// </summary>
    /// <remarks>
    /// It decides the interceptor's signature, which has to match the method it intercepts exactly.
    /// The argument itself is not read: the verb was already resolved at build time, because it is
    /// written into the handler's own information.
    /// </remarks>
    public bool NamesVerb { get; }

    /// <summary>
    /// The <c>[InterceptsLocation]</c> the compiler itself wrote for this call site.
    /// </summary>
    /// <remarks>
    /// Taken from <c>SemanticModel.GetInterceptableLocation</c> rather than assembled here. The
    /// older spelling was a file path and a character offset, so every edit above a registration
    /// moved it and the interceptor silently stopped intercepting.
    /// </remarks>
    public string InterceptsAttribute { get; }

    /// <summary>The lambda's natural type, which the generated handler is built against.</summary>
    public ITypeDefinition DelegateType { get; }

    public RequestHandlerModel Handler { get; }

    /// <summary>The path tokens the emitted binder reads, by name.</summary>
    public IReadOnlyList<string> BoundTokens { get; }

    public bool Equals(LambdaRouteModel? other) =>
        other != null
        && Method == other.Method
        && NamesVerb == other.NamesVerb
        && InterceptsAttribute == other.InterceptsAttribute
        && Equals(DelegateType, other.DelegateType)
        && Handler.Equals(other.Handler)
        && BoundTokens.Count == other.BoundTokens.Count
        && !BoundTokens.Where((name, index) => name != other.BoundTokens[index]).Any();

    public override bool Equals(object obj) => Equals(obj as LambdaRouteModel);

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = Method.GetHashCode();

            hash = (hash * 397) ^ InterceptsAttribute.GetHashCode();
            hash = (hash * 397) ^ Handler.GetHashCode();

            return hash;
        }
    }
}
