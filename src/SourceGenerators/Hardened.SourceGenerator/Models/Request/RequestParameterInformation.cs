using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Models.Request;

public class RequestParameterInformation {
    public RequestParameterInformation(
        ITypeDefinition parameterType,
        string name,
        bool required,
        string? defaultValue,
        ParameterBindType bindingType,
        string bindingName,
        int parameterIndex,
        AttributeModel? customAttribute = null) {
        ParameterType = parameterType;
        Name = name;
        Required = required;
        DefaultValue = defaultValue;
        BindingType = bindingType;
        BindingName = bindingName;
        ParameterIndex = parameterIndex;
        CustomAttribute = customAttribute;
    }

    public ITypeDefinition ParameterType { get; }

    public string Name { get; }

    public bool Required { get; }

    public string? DefaultValue { get; }
    
    public AttributeModel? CustomAttribute { get; }

    public ParameterBindType BindingType { get; }

    public string BindingName { get; }

    /// <summary>
    /// The prose the contract carries for this parameter, or null.
    /// </summary>
    /// <remarks>
    /// Settable rather than a constructor argument: every call site builds one of these positionally
    /// and most have nothing to say here. A description reaches the published document and nothing
    /// else - the binder does not read it.
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// The contract's declaration of this parameter, when the handler came from one.
    /// </summary>
    /// <remarks>
    /// The document writer prefers these facts - the declared wire type, the constraint bounds,
    /// the enum vocabulary, the default - over re-deriving a schema from
    /// <see cref="ParameterType"/>, which can only guess. The binder does not read it: binding and
    /// validation are generated from the spec model before it is narrowed to this type, so a
    /// handler with no contract behaves exactly as it did when this was absent.
    /// </remarks>
    internal ParameterModel? SpecParameter { get; set; }

    public int ParameterIndex {
        get;
    }

    public override bool Equals(object obj) {
        if (obj is not RequestParameterInformation requestParameterInformation) {
            return false;
        }

        if (!ParameterType.Equals(requestParameterInformation.ParameterType)) {
            return false;
        }

        if (!Name.Equals(requestParameterInformation.Name)) {
            return false;
        }

        if (!Required.Equals(requestParameterInformation.Required)) {
            return false;
        }

        if (DefaultValue != requestParameterInformation.DefaultValue) {
            return false;
        }

        if (!BindingType.Equals(requestParameterInformation.BindingType)) {
            return false;
        }

        if (!BindingName.Equals(requestParameterInformation.BindingName)) {
            return false;
        }

        if (CustomAttribute != null &&
            !CustomAttribute.Equals(requestParameterInformation.CustomAttribute)) {
            return false;
        }

        // Both reach the published document and nothing else. Left out of equality, an edit to a
        // parameter's prose or its declared constraints compares equal and the cached document
        // keeps the old text - the exact staleness ResponseModelSelector's remarks warn about.
        if (Description != requestParameterInformation.Description) {
            return false;
        }

        if (!Equals(SpecParameter, requestParameterInformation.SpecParameter)) {
            return false;
        }

        return true;
    }

    public override string ToString() {
        return $"{ParameterType} {Name}";
    }

    public override int GetHashCode() {
        unchecked {
            var hashCode = ParameterType.GetHashCode();
            hashCode = (hashCode * 397) ^ Name.GetHashCode();
            hashCode = (hashCode * 397) ^ Required.GetHashCode();
            hashCode = (hashCode * 397) ^ (DefaultValue != null ? DefaultValue.GetHashCode() : 0);
            hashCode = (hashCode * 397) ^ (int)BindingType;
            hashCode = (hashCode * 397) ^ BindingName.GetHashCode();
            
            if (CustomAttribute is not null) {
                hashCode = (hashCode * 397) ^ CustomAttribute.GetHashCode();
            }
            
            return hashCode;
        }
    }
}

public enum ParameterBindType {
    Path,
    QueryString,
    Header,
    Cookie,

    /// <summary>
    /// A field of an <c>application/x-www-form-urlencoded</c> body, named by <c>[FromForm]</c>.
    /// </summary>
    /// <remarks>
    /// Explicit rather than inferred. A parameter the route does not declare falls to
    /// <see cref="Body"/>, and quietly changing that to a form field when the content type happens
    /// to be a form would make a handler's binding depend on what the caller sent.
    /// </remarks>
    Form,
    Body,
    ServiceProvider,
    FromServiceProvider,
    ExecutionContext,
    ExecutionRequest,
    ExecutionResponse,

    /// <summary>
    /// The request's <c>CancellationToken</c>, taken off the context.
    /// </summary>
    /// <remarks>
    /// A handler does not have to ask for this to be cancellable - the pipeline already passes
    /// <c>context.CancellationToken</c> to <c>WithCancellation</c> where it enumerates a streamed
    /// response, and hands it to filters. Binding it exists because a handler that wants to pass it
    /// on to something else has to be able to name it, and because
    /// <c>[EnumeratorCancellation] CancellationToken</c> is what every C# author writes on an async
    /// iterator - which, without this, bound as a body parameter and failed at run time.
    /// </remarks>
    CancellationToken,
    CustomAttribute,

    /// <summary>
    /// The parameter's type could not be resolved, so there is nothing to bind it from.
    ///
    /// <para>
    /// A handler carrying one of these is not generated — see
    /// <c>RequestHandlerModelExtensions.UnresolvedParameter</c>. It exists so the model stays a
    /// value the transform can always produce: a syntax provider cannot report a diagnostic, so
    /// the problem has to be carried forward and reported when source is written.
    /// </para>
    /// </summary>
    Unresolved,
}