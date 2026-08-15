using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Models.Request;

public class RequestHandlerModel {
    public RequestHandlerModel(
        RequestHandlerNameModel name,
        ITypeDefinition controllerType,
        string handlerMethod,
        ITypeDefinition invokeHandlerType,
        IReadOnlyList<RequestParameterInformation> requestParameterInformationList,
        ResponseInformationModel responseInformation,
        IReadOnlyList<AttributeModel> filters) {
        Name = name;
        ControllerType = controllerType;
        HandlerMethod = handlerMethod;
        InvokeHandlerType = invokeHandlerType;
        RequestParameterInformationList = requestParameterInformationList;
        ResponseInformation = responseInformation;
        Filters = filters;
    }

    public RequestHandlerNameModel Name { get; }

    public ITypeDefinition ControllerType { get; }

    public string HandlerMethod { get; }

    public ITypeDefinition InvokeHandlerType { get; }

    public IReadOnlyList<RequestParameterInformation> RequestParameterInformationList { get; }

    public ResponseInformationModel ResponseInformation { get; }

    public IReadOnlyList<AttributeModel> Filters { get; }

    /// <summary>
    /// An interface the generated <c>Parameters</c> class implements, or null.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set by the OpenAPI generator, which is told the name by the build task. That task emits the
    /// interface and a validator over it, but cannot name <c>Parameters</c> itself - it is nested
    /// inside a handler type whose name carries a computed suffix, which only the generator knows.
    /// So the interface is the seam: the task names it, the generator implements it.
    /// </para>
    /// <para>
    /// A property rather than a constructor parameter because this model is shared with the web and
    /// function generators, which have no spec and nothing to put here.
    /// </para>
    /// </remarks>
    public ITypeDefinition? ParametersInterface { get; set; }

    /// <summary>
    /// The generated validator for this handler's <c>Parameters</c> class, or null when it declares
    /// no constraints.
    /// </summary>
    /// <remarks>
    /// Carried so the entry point can register it. Nothing else can: the validator is emitted by
    /// <c>HandlerValidationGenerator</c> one handler at a time, and registration has to be written
    /// once per entry point, so the name has to travel with the handler to reach the generator that
    /// writes the dependency-injection method.
    /// </remarks>
    public ITypeDefinition? ParametersValidator { get; set; }

    /// <summary>
    /// The response body's JSON Schema, captured while the Roslyn symbol still existed.
    /// </summary>
    /// <remarks>
    /// This model records types as <c>ITypeDefinition</c> - a namespace and a name - so by the time
    /// a document is written the members are gone. Converting during the transform and carrying the
    /// text forward is what lets the reverse direction describe a body at all. Null for a generator
    /// that emits no document.
    /// </remarks>
    public HandlerSchema? ResponseSchema { get; set; }

    /// <summary>The request body's JSON Schema, on the same terms.</summary>
    public HandlerSchema? RequestSchema { get; set; }

    /// <summary>
    /// The OpenAPI tag this operation is grouped under, when the controller declared one with
    /// <c>[Tag]</c>. Null means the default derivation applies - see
    /// <c>OpenApiDocumentGenerator.Tag</c>.
    /// </summary>
    /// <remarks>
    /// A property rather than a constructor parameter, on the same terms as
    /// <see cref="ParametersInterface"/>: the model is shared with the function and console
    /// generators, and neither has tags or a document to put them in.
    /// </remarks>
    public string? Tag { get; set; }

    /// <summary>
    /// The handler's <c>&lt;summary&gt;</c> doc comment, as the operation's <c>summary</c>.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// The handler's <c>&lt;remarks&gt;</c> doc comment, as the operation's <c>description</c>.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the handler or its controller carries <c>[Obsolete]</c>, as the operation's
    /// <c>deprecated</c>. A client generated from the document then warns where the application
    /// warns, instead of the deprecation stopping at the assembly boundary.
    /// </summary>
    public bool IsDeprecated { get; set; }

    public override bool Equals(object obj) {
        if (obj is not RequestHandlerModel requestHandlerModel) {
            return false;
        }

        if (!Name.Equals(requestHandlerModel.Name)) {
            return false;
        }

        if (!ControllerType.Equals(requestHandlerModel.ControllerType)) {
            return false;
        }

        if (!HandlerMethod.Equals(requestHandlerModel.HandlerMethod)) {
            return false;
        }


        if (!InvokeHandlerType.Equals(requestHandlerModel.InvokeHandlerType)) {
            return false;
        }

        if (!ResponseInformation.Equals(requestHandlerModel.ResponseInformation)) {
            return false;
        }

        if (Filters.Count != requestHandlerModel.Filters.Count) {
            return false;
        }

        for (var i = 0; i < requestHandlerModel.Filters.Count; i++) {
            var x = Filters[i];
            var y = requestHandlerModel.Filters[i];

            if (!x.Equals(y)) {
                return false;
            }
        }

        if (RequestParameterInformationList.Count != requestHandlerModel.RequestParameterInformationList.Count) {
            return false;
        }

        for (var i = 0; i < RequestParameterInformationList.Count; i++) {
            var x = RequestParameterInformationList[i];
            var y = requestHandlerModel.RequestParameterInformationList[i];

            if (!x.Equals(y)) {
                return false;
            }
        }

        if (!Equals(ParametersInterface, requestHandlerModel.ParametersInterface)) {
            return false;
        }

        if (!Equals(ParametersValidator, requestHandlerModel.ParametersValidator)) {
            return false;
        }

        if (!string.Equals(Tag, requestHandlerModel.Tag, StringComparison.Ordinal)) {
            return false;
        }

        if (!string.Equals(Summary, requestHandlerModel.Summary, StringComparison.Ordinal)) {
            return false;
        }

        if (!string.Equals(Description, requestHandlerModel.Description, StringComparison.Ordinal)) {
            return false;
        }

        if (IsDeprecated != requestHandlerModel.IsDeprecated) {
            return false;
        }

        return true;
    }

    public override string ToString() {
        var stringBuilder = new StringBuilder();

        stringBuilder.Append(Name);
        stringBuilder.Append(":");
        stringBuilder.Append(ControllerType);
        stringBuilder.Append(".");
        stringBuilder.Append(HandlerMethod);

        return stringBuilder.ToString();
    }

    public override int GetHashCode() {
        unchecked {
            var hashCode = Name.GetHashCode();

            hashCode = (hashCode * 397) ^ ControllerType.GetHashCode();
            hashCode = (hashCode * 397) ^ HandlerMethod.GetHashCode();
            hashCode = (hashCode * 397) ^ InvokeHandlerType.GetHashCode();
            hashCode = (hashCode * 397) ^ RequestParameterInformationList.GetHashCodeAggregation();
            hashCode = (hashCode * 397) ^ ResponseInformation.GetHashCode();
            hashCode = (hashCode * 397) ^ Filters.GetHashCodeAggregation();
            hashCode = (hashCode * 397) ^ (Tag?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ (Summary?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ (Description?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ IsDeprecated.GetHashCode();

            return hashCode;
        }
    }
}