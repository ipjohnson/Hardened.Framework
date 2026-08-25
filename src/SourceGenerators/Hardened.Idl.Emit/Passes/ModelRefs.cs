using System;
using System.Collections.Generic;
using Hardened.Generation.Models;

namespace Hardened.Idl;

/// <summary>
/// Every place a model holds a <c>$ref</c>, in one list.
/// </summary>
/// <remarks>
/// <para>
/// There are thirteen of them, and every pass that walks references needs all thirteen. Two passes used
/// to enumerate them by hand and one listed five: Cloudflare and PagerDuty reference hundreds of
/// schemas that produce no type, and the references the incomplete pass never visited - parameters,
/// declared error responses, base types, discriminator branches - each became a CS0234 naming
/// something nothing declares.
/// </para>
/// <para>
/// Yielded as settable handles rather than strings, so a caller can rewrite in place. That is what
/// lets renaming a schema and clearing an unusable reference share one traversal instead of keeping
/// two lists in step.
/// </para>
/// </remarks>
internal static class ModelRefs {

    /// <summary>A reference the caller may read or replace.</summary>
    internal readonly struct Handle {
        private readonly Action<string?> _set;

        public Handle(string? value, Action<string?> set) {
            Value = value;
            _set = set;
        }

        public string? Value { get; }

        public void Set(string? value) => _set(value);
    }

    public static IEnumerable<Handle> All(ServiceSpecModel model) {
        foreach (var schema in model.Schemas) {
            var captured = schema;

            yield return new Handle(schema.BaseRef, value => captured.BaseRef = value);
            yield return new Handle(schema.ArrayItemsRef, value => captured.ArrayItemsRef = value);

            foreach (var branch in schema.OneOf) {
                var branchCaptured = branch;

                yield return new Handle(branch.Ref, value => branchCaptured.Ref = value);
            }

            foreach (var mapping in schema.DiscriminatorMapping) {
                var mappingCaptured = mapping;

                yield return new Handle(mapping.Ref, value => mappingCaptured.Ref = value ?? "");
            }

            foreach (var property in schema.Properties) {
                var propertyCaptured = property;

                yield return new Handle(property.Ref, value => propertyCaptured.Ref = value);
                yield return new Handle(
                    property.ArrayItemsRef, value => propertyCaptured.ArrayItemsRef = value);
                yield return new Handle(
                    property.DictionaryValueRef, value => propertyCaptured.DictionaryValueRef = value);

                foreach (var branch in property.OneOf) {
                    var branchCaptured = branch;

                    yield return new Handle(branch.Ref, value => branchCaptured.Ref = value);
                }
            }
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var captured = operation;

                yield return new Handle(
                    operation.RequestBodyRef, value => captured.RequestBodyRef = value);
                yield return new Handle(
                    operation.ResponseRef, value => captured.ResponseRef = value);
                yield return new Handle(
                    operation.ResponseArrayItemsRef, value => captured.ResponseArrayItemsRef = value);

                foreach (var error in operation.ErrorResponses) {
                    var errorCaptured = error;

                    yield return new Handle(error.Ref, value => errorCaptured.Ref = value);
                }

                foreach (var parameter in operation.Parameters) {
                    var parameterCaptured = parameter;

                    yield return new Handle(parameter.Ref, value => parameterCaptured.Ref = value);
                    yield return new Handle(
                        parameter.ArrayItemsRef, value => parameterCaptured.ArrayItemsRef = value);
                }
            }
        }
    }
}
