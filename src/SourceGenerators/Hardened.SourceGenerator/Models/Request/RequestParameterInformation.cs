using CSharpAuthor;
using Hardened.Generation;
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
        AttributeModel? customAttribute = null,
        bool constructorRequiresServices = false,
        bool registeredAsService = false) {
        ConstructorRequiresServices = constructorRequiresServices;
        RegisteredAsService = registeredAsService;


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

    /// <summary>The name the parameter was declared with, and the name a caller uses for it.</summary>
    /// <remarks>
    /// Not necessarily a C# identifier - a route token or a parameter can be spelled <c>base</c>,
    /// which is a keyword. <see cref="MemberName"/> is the identifier; this is the wire name, and
    /// <see cref="BindingName"/> replaces it when an attribute renames the parameter.
    /// </remarks>
    public string Name { get; }

    /// <summary>
    /// The C# identifier for this parameter: <see cref="Name"/>, escaped when it is a keyword.
    /// </summary>
    /// <remarks>
    /// Everything the generator emits as an identifier reads this - the Parameters property, the
    /// binder's assignment target, the handler argument, a link method's parameter. Everything that
    /// emits a string a caller sent reads <see cref="Name"/> or <see cref="BindingName"/> instead,
    /// because <c>@base</c> is neither a query key nor the field a validation error names.
    ///
    /// <para>
    /// The spec-first side gets this from <c>NameAllocator</c>, which escapes as it allocates. The
    /// code-first side had no equivalent, so a route token named after a keyword emitted a
    /// parameter declaration that did not compile.
    /// </para>
    /// </remarks>
    public string MemberName => NamingHelper.EscapeIdentifier(Name);

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

    /// <summary>
    /// The JSON Schema facets the constraints written on this parameter imply, as the inside of
    /// an object, or null when it declares none the document can say.
    /// </summary>
    /// <remarks>
    /// Read where the parameter's symbol is, in the syntax transform, and spliced into the schema
    /// where the schema is written, at the output stage - the same journey
    /// <see cref="Description"/> makes. Set for a hand-written handler only, and carried through
    /// the description the web generator writes for it, since the shared builder rebuilds every
    /// parameter from that description. A parameter that came from a contract states its facets
    /// in <see cref="SpecParameter"/>'s typed members instead, which the writer prefers.
    /// </remarks>
    public string? SchemaFacets { get; set; }

    /// <summary>
    /// Whether <c>[Required]</c> is written on the parameter, so a nullable parameter the caller
    /// must still send is published as required.
    /// </summary>
    public bool RequiredByConstraint { get; set; }

    /// <summary>
    /// Whether every public constructor of <see cref="ParameterType"/> takes an interface.
    /// </summary>
    /// <remarks>
    /// Recorded at the syntax transform, where the semantic model is in hand, and read at the
    /// output stage, where a diagnostic can be reported. A type shaped like that has no reading as
    /// a request body: the deserializer cannot construct an interface, so a body parameter typed
    /// this way is a service the author meant to inject. Same carry-forward as
    /// <see cref="ParameterBindType.Unresolved"/>, for the same reason - a syntax provider cannot
    /// report a diagnostic.
    /// </remarks>
    public bool ConstructorRequiresServices { get; }

    /// <summary>
    /// Whether the parameter's type carries a DependencyModules registration -
    /// <c>[SingletonService]</c>, <c>[ScopedService]</c> or <c>[TransientService]</c> - and so is
    /// a service whatever its constructors look like. Asked only of a parameter that fell to the
    /// body; the other half of what <c>HRDR007</c> reads.
    /// </summary>
    public bool RegisteredAsService { get; }

    public int ParameterIndex {
        get;
    }

    /// <summary>
    /// The same parameter at a different position, with everything else carried across.
    /// </summary>
    /// <remarks>
    /// Exists for the reason <c>RequestHandlerModel.WithFilters</c> does. Rebuilt by hand this
    /// dropped the parameter's prose, its contract declaration and whether its type can only be
    /// constructed from services, and nothing failed when it did - the last of those silently
    /// turned off the diagnostic that reports it. Adding a property here is now the only place that
    /// has to change.
    /// </remarks>
    public RequestParameterInformation WithIndex(int parameterIndex) =>
        new(ParameterType,
            Name,
            Required,
            DefaultValue,
            BindingType,
            BindingName,
            parameterIndex,
            CustomAttribute,
            ConstructorRequiresServices,
            RegisteredAsService) {
            Description = Description,
            SpecParameter = SpecParameter,
            SchemaFacets = SchemaFacets,
            RequiredByConstraint = RequiredByConstraint
        };

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

        if (SchemaFacets != requestParameterInformation.SchemaFacets ||
            RequiredByConstraint != requestParameterInformation.RequiredByConstraint) {
            return false;
        }

        if (ConstructorRequiresServices != requestParameterInformation.ConstructorRequiresServices) {
            return false;
        }

        if (RegisteredAsService != requestParameterInformation.RegisteredAsService) {
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

            hashCode = (hashCode * 397) ^ ConstructorRequiresServices.GetHashCode();
            hashCode = (hashCode * 397) ^ RegisteredAsService.GetHashCode();
            
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