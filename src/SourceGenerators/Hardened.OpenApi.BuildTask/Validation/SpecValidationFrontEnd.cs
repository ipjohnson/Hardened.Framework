using System.Collections.Generic;
using System.Linq;
using Hardened.OpenApi.SourceGenerator;
using Hardened.OpenApi.SourceGenerator.Models;
using ValidationModules.SourceGenerator.Impl;
using ValidationModules.SourceGenerator.Impl.Models;

namespace Hardened.OpenApi.BuildTask.Validation;

/// <summary>
/// Turns an OpenAPI document into ValidationModules' IR.
/// </summary>
/// <remarks>
/// <para>
/// The second front-end. ValidationModules' own reads attributes off symbols with Roslyn; this reads
/// a spec. They meet at <see cref="ValidatedTypeModel"/> and share one emitter, which is what stops
/// a spec-declared <c>maxLength</c> and an attribute-declared <c>[StringLength]</c> producing
/// different code, different field paths or different messages.
/// </para>
/// <para>
/// <b>Field names are wire names.</b> A path parameter is <c>petId</c>, not <c>PetId</c>, and a body
/// property is whatever the schema called it. The body is reached by descending into a <c>body</c>
/// property, so its errors path as <c>body.name</c> - which distinguishes them from a path parameter
/// of the same name, something the flat naming this replaces could not do. Parameters stay bare:
/// path and query are both the URL as far as a caller is concerned, and making them say so would be
/// a distinction the caller has to decode without wanting it.
/// </para>
/// </remarks>
internal static class SpecValidationFrontEnd {

    /// <summary>Everything one spec needs emitted for validation.</summary>
    internal sealed record Result(
        IReadOnlyList<OperationValidation> Operations,
        IReadOnlyList<ValidatedTypeModel> Validators,
        PatternRegistry Patterns);

    /// <summary>One operation's parameter interface and the validator over it.</summary>
    internal sealed record OperationValidation(
        string OperationId,
        string InterfaceName,
        string ValidatorName,
        IReadOnlyList<InterfaceMember> Members);

    /// <summary>One member of a generated parameter interface.</summary>
    /// <param name="Name">The C# name, matching what the handler's Parameters class declares.</param>
    /// <param name="TypeName">Its fully qualified type.</param>
    internal sealed record InterfaceMember(string Name, string TypeName);

    public static Result Build(OpenApiSpecModel spec, string rootNamespace) {
        var modelsNamespace = rootNamespace + ".Models";
        var validationNamespace = rootNamespace + ".Validation";

        var patterns = new PatternRegistry(validationNamespace, spec.FileName);
        var operations = new List<OperationValidation>();
        var validators = new List<ValidatedTypeModel>();
        var bodyValidators = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var service in spec.Services) {
            foreach (var operation in service.Operations) {
                var validation = BuildOperation(
                    operation, spec, modelsNamespace, validationNamespace, patterns, bodyValidators, validators);

                if (validation != null) {
                    operations.Add(validation);
                }
            }
        }

        return new Result(operations, validators, patterns);
    }

    private static OperationValidation? BuildOperation(
        OperationModel operation,
        OpenApiSpecModel spec,
        string modelsNamespace,
        string validationNamespace,
        PatternRegistry patterns,
        Dictionary<string, string> bodyValidators,
        List<ValidatedTypeModel> validators) {
        var members = new List<InterfaceMember>();
        var properties = new List<ValidatedPropertyModel>();

        foreach (var parameter in operation.Parameters) {
            // Header parameters are not bound onto the parameters class, so there is nothing to
            // validate - see finding 3.6.
            if (parameter.In != "path" && parameter.In != "query") {
                continue;
            }

            var csType = TypeMapper.MapParameterToCSharpType(parameter);
            var typeName = QualifiedTypeName(csType, modelsNamespace, !parameter.IsRequired);
            var name = NamingHelper.ToParameterName(parameter.Name);

            members.Add(new InterfaceMember(name, typeName));

            if (!parameter.HasValidationConstraints) {
                continue;
            }

            properties.Add(new ValidatedPropertyModel(
                PropertyName: name,
                FieldName: parameter.Name,
                TypeName: typeName,
                Shape: PropertyShape.Scalar,
                ElementTypeName: null,
                ElementValidatorName: null,
                IsReferenceType: IsReferenceType(csType),
                IsString: csType == "string",
                IsNullableValueType: !parameter.IsRequired && !IsReferenceType(csType),
                IsIndexable: false,
                CountAccessor: "Count",
                ValidateNested: false,
                Constraints: Constraints.ForParameter(parameter, patterns).ToEquatableArray()));
        }

        var bodySchema = BodySchema(operation, spec);

        if (bodySchema != null) {
            var bodyType = "global::" + modelsNamespace + "." + NamingHelper.ToPascalCase(bodySchema.Name);

            members.Add(new InterfaceMember("body", bodyType));

            var bodyValidator = BodyValidator(
                bodySchema, spec, modelsNamespace, validationNamespace, patterns, bodyValidators, validators);

            if (bodyValidator != null) {
                // Descending gives errors a "body." prefix, which is what tells a caller a failure
                // came from the payload rather than from a path parameter of the same name.
                properties.Add(new ValidatedPropertyModel(
                    PropertyName: "body",
                    FieldName: "body",
                    TypeName: bodyType,
                    Shape: PropertyShape.Object,
                    ElementTypeName: null,
                    ElementValidatorName: bodyValidator,
                    IsReferenceType: true,
                    IsString: false,
                    IsNullableValueType: false,
                    IsIndexable: false,
                    CountAccessor: "Count",
                    ValidateNested: true,
                    Constraints: EquatableArray<ConstraintModel>.Empty));
            }
        }

        // Nothing constrained means no interface, no validator and no filter on the handler.
        if (properties.Count == 0) {
            return null;
        }

        var interfaceName = "I" + NamingHelper.ToPascalCase(operation.OperationId) + "Parameters";
        var validatorName = NamingHelper.ToPascalCase(operation.OperationId) + "ParametersValidator";

        validators.Add(new ValidatedTypeModel(
            Namespace: validationNamespace,
            TypeName: interfaceName,
            QualifiedTypeName: "global::" + validationNamespace + "." + interfaceName,
            ValidatorName: validatorName,
            Properties: properties.ToEquatableArray()));

        return new OperationValidation(operation.OperationId, interfaceName, validatorName, members);
    }

    /// <summary>
    /// The validator for a request body schema, built once per schema however many operations use it.
    /// </summary>
    private static string? BodyValidator(
        SchemaModel schema,
        OpenApiSpecModel spec,
        string modelsNamespace,
        string validationNamespace,
        PatternRegistry patterns,
        Dictionary<string, string> built,
        List<ValidatedTypeModel> validators) {
        var typeName = NamingHelper.ToPascalCase(schema.Name);

        if (built.TryGetValue(typeName, out var existing)) {
            return existing;
        }

        var properties = new List<ValidatedPropertyModel>();

        foreach (var property in schema.Properties) {
            var csType = TypeMapper.MapPropertyToCSharpType(property);
            var required = schema.Required.Contains(property.Name) || property.IsRequired;
            var propertyType = QualifiedTypeName(csType, modelsNamespace, !required);

            if (property.Ref != null) {
                var nested = spec.Schemas.FirstOrDefault(
                    s => s.Name == TypeMapper.GetRefName(property.Ref) && s.Kind == SchemaKind.Object);

                if (nested != null) {
                    var nestedValidator = BodyValidator(
                        nested, spec, modelsNamespace, validationNamespace, patterns, built, validators);

                    if (nestedValidator != null) {
                        properties.Add(new ValidatedPropertyModel(
                            PropertyName: NamingHelper.ToPascalCase(property.Name),
                            FieldName: property.Name,
                            TypeName: propertyType,
                            Shape: PropertyShape.Object,
                            ElementTypeName: null,
                            ElementValidatorName: nestedValidator,
                            IsReferenceType: true,
                            IsString: false,
                            IsNullableValueType: false,
                            IsIndexable: false,
                            CountAccessor: "Count",
                            ValidateNested: true,
                            Constraints: Constraints.ForProperty(property, required, patterns).ToEquatableArray()));
                        continue;
                    }
                }
            }

            if (!property.HasValidationConstraints && !required) {
                continue;
            }

            properties.Add(new ValidatedPropertyModel(
                PropertyName: NamingHelper.ToPascalCase(property.Name),
                FieldName: property.Name,
                TypeName: propertyType,
                Shape: PropertyShape.Scalar,
                ElementTypeName: null,
                ElementValidatorName: null,
                IsReferenceType: IsReferenceType(csType),
                IsString: csType == "string",
                IsNullableValueType: !required && !IsReferenceType(csType),
                IsIndexable: property.IsArray,
                CountAccessor: "Count",
                ValidateNested: false,
                Constraints: Constraints.ForProperty(property, required, patterns).ToEquatableArray()));
        }

        if (properties.Count == 0) {
            return null;
        }

        var validatorName = typeName + "Validator";

        // Recorded before recursing further, so a schema that reaches itself resolves to the
        // validator being built rather than recursing until the stack runs out.
        built[typeName] = validatorName;

        validators.Add(new ValidatedTypeModel(
            Namespace: validationNamespace,
            TypeName: typeName,
            QualifiedTypeName: "global::" + modelsNamespace + "." + typeName,
            ValidatorName: validatorName,
            Properties: properties.ToEquatableArray()));

        return validatorName;
    }

    private static SchemaModel? BodySchema(OperationModel operation, OpenApiSpecModel spec) {
        if (operation.RequestBodyRef == null) {
            return null;
        }

        var name = TypeMapper.GetRefName(operation.RequestBodyRef);

        return spec.Schemas.FirstOrDefault(s => s.Name == name && s.Kind == SchemaKind.Object);
    }

    private static string QualifiedTypeName(string csType, string modelsNamespace, bool nullable) {
        var qualified = csType switch {
            "string" or "int" or "uint" or "long" or "float" or "double" or "bool" => csType,
            "DateTime" => "global::System.DateTime",
            "DateOnly" => "global::System.DateOnly",
            "JsonElement" => "global::System.Text.Json.JsonElement",
            "byte[]" => "byte[]",
            _ when csType.StartsWith("List<") => "global::System.Collections.Generic." + csType,
            _ when csType.StartsWith("Dictionary<") => "global::System.Collections.Generic." + csType,
            _ => "global::" + modelsNamespace + "." + csType,
        };

        return nullable ? qualified + "?" : qualified;
    }

    private static bool IsReferenceType(string csType) =>
        csType == "string" || csType == "byte[]" ||
        csType.StartsWith("List<") || csType.StartsWith("Dictionary<") ||
        !IsPrimitive(csType);

    private static bool IsPrimitive(string csType) => csType switch {
        "int" or "uint" or "long" or "float" or "double" or "bool" or "DateTime" or "DateOnly"
            or "JsonElement" => true,
        _ => false,
    };
}
