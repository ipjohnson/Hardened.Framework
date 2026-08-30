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

    /// <summary>
    /// The same handler with a different filter list, and everything else carried across.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists so that adding a filter cannot silently drop anything. The validation generator used
    /// to rebuild the model by hand and carried two of the eight settable properties, so every
    /// handler that gained a validator lost its request schema, its response schema, its
    /// <c>[Tag]</c>, its summary, its description and its deprecation flag. In the served document
    /// that read as a write operation with no <c>requestBody</c> and no response content — the two
    /// things a client most needs — on exactly the operations whose models were best described.
    /// </para>
    /// <para>
    /// The defect was in the shape rather than in the line: a hand-rolled copy has to be revisited
    /// every time a property is added, and nothing fails when it is not. Adding a property here is
    /// now the only place that has to change.
    /// </para>
    /// </remarks>
    public RequestHandlerModel WithFilters(IReadOnlyList<AttributeModel> filters) =>
        new(Name,
            ControllerType,
            HandlerMethod,
            InvokeHandlerType,
            RequestParameterInformationList,
            ResponseInformation,
            filters) {
            ParametersInterface = ParametersInterface,
            ParametersValidator = ParametersValidator,
            ResponseSchema = ResponseSchema,
            ResponseSchemas = ResponseSchemas,
            DeclaredResponsesAreComplete = DeclaredResponsesAreComplete,
            RequestSchema = RequestSchema,
            Tag = Tag,
            Summary = Summary,
            Description = Description,
            IsDeprecated = IsDeprecated,
            // Every settable member, because this copy is what enrichment hands downstream - a
            // field left out here is a field the document loses only when the application has
            // [Handler] filters, which is the worst kind of sometimes.
            SecurityRequirements = SecurityRequirements,
            HasGeneratedValidation = HasGeneratedValidation
        };

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

    /// <summary>
    /// Every response the handler declares, when its return type declares a set of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Empty for a handler that returns one type, which is every handler that existed before
    /// response sets did - and the document writer falls back to <see cref="ResponseSchema"/> for
    /// those, so nothing about their document changes.
    /// </para>
    /// <para>
    /// Beside <see cref="ResponseSchema"/> rather than replacing it. The streamed-response path
    /// reads that one to write an <c>itemSchema</c>, which is a different question - the shape of
    /// one item of many, not one response of several - and folding the two would make the writer
    /// ask which meaning it held.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Whether <see cref="ResponseSchemas"/> is every status the handler answers.
    /// </summary>
    /// <remarks>
    /// True for a described operation and for a Response or union return type: both name the whole
    /// set, success included - and that success need not be 200, since Response&lt;NoContent,
    /// NotFound&gt; declares 204 and 404 and nothing else. False when the only declarations came
    /// from [Throws&lt;T&gt;], which names failures while the success still comes from the return
    /// type, so the document has to write that one as well.
    /// </remarks>
    public bool DeclaredResponsesAreComplete { get; set; } = true;

    public IReadOnlyList<ResponseSchemaModel> ResponseSchemas { get; set; } =
        Array.Empty<ResponseSchemaModel>();

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

    /// <summary>
    /// Whether a generated validator answers 400 before this handler runs - a parameter
    /// interface, or constraints on the bound body.
    /// </summary>
    /// <remarks>
    /// For the published document, which declares the validation response only where one can
    /// actually happen; declaring it everywhere would be the widening the contract checks exist
    /// to prevent.
    /// </remarks>
    public bool HasGeneratedValidation { get; set; }

    /// <summary>
    /// The contract's declared security, one OpenAPI requirement object of JSON per entry.
    /// </summary>
    /// <remarks>
    /// For the published document; enforcement travels as authorization filters. Empty for a
    /// handler whose contract declared none, and always empty for code-first, which has no way to
    /// declare a scheme yet.
    /// </remarks>
    public IReadOnlyList<string> SecurityRequirements { get; set; } =
        System.Array.Empty<string>();

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

        if (!ResponseSchemas.SequenceEqual(requestHandlerModel.ResponseSchemas)) {
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

        if (SecurityRequirements.Count != requestHandlerModel.SecurityRequirements.Count) {
            return false;
        }

        for (var i = 0; i < SecurityRequirements.Count; i++) {
            if (!string.Equals(
                    SecurityRequirements[i], requestHandlerModel.SecurityRequirements[i],
                    StringComparison.Ordinal)) {
                return false;
            }
        }

        if (!string.Equals(Description, requestHandlerModel.Description, StringComparison.Ordinal)) {
            return false;
        }

        if (IsDeprecated != requestHandlerModel.IsDeprecated) {
            return false;
        }

        if (HasGeneratedValidation != requestHandlerModel.HasGeneratedValidation) {
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
            hashCode = (hashCode * 397) ^ ResponseSchemas.GetHashCodeAggregation();
            hashCode = (hashCode * 397) ^ Filters.GetHashCodeAggregation();
            hashCode = (hashCode * 397) ^ (Tag?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ (Summary?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ (Description?.GetHashCode() ?? 0);
            hashCode = (hashCode * 397) ^ IsDeprecated.GetHashCode();

            return hashCode;
        }
    }
}