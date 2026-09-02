using System;
using System.Collections.Generic;
using Hardened.Generation.Models;

namespace Hardened.Idl;

/// <summary>
/// Every place a model holds a <c>$ref</c>, in one list.
/// </summary>
/// <remarks>
/// <para>
/// There are fifteen of them, and every pass that walks references needs all fifteen. Two passes used
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

        public Handle(string? value, string location, Action<string?> set) {
            Value = value;
            Location = location;
            _set = set;
        }

        public string? Value { get; }

        /// <summary>Where the reference was made, for a message the author can act on.</summary>
        /// <remarks>
        /// Carried rather than derived: the two passes that rewrite references do not need it, and
        /// the one that reports a reference naming nothing cannot reconstruct it - by then the
        /// handle is all there is.
        /// </remarks>
        public string Location { get; }

        public void Set(string? value) => _set(value);
    }

    public static IEnumerable<Handle> All(ServiceSpecModel model) {
        foreach (var schema in model.Schemas) {
            var captured = schema;

            yield return new Handle(
                schema.BaseRef, schema.Name + " (base type)", value => captured.BaseRef = value);
            yield return new Handle(
                schema.ArrayItemsRef, schema.Name + " (items)",
                value => captured.ArrayItemsRef = value);

            foreach (var branch in schema.OneOf) {
                var branchCaptured = branch;

                yield return new Handle(
                    branch.Ref, schema.Name + " (oneOf branch)", value => branchCaptured.Ref = value);
            }

            foreach (var mapping in schema.DiscriminatorMapping) {
                var mappingCaptured = mapping;

                yield return new Handle(
                    mapping.Ref, schema.Name + " (discriminator mapping)",
                    value => mappingCaptured.Ref = value ?? "");
            }

            foreach (var property in schema.Properties) {
                var propertyCaptured = property;
                var where = schema.Name + "." + property.Name;

                yield return new Handle(property.Ref, where, value => propertyCaptured.Ref = value);
                yield return new Handle(
                    property.ArrayItemsRef, where + " (items)",
                    value => propertyCaptured.ArrayItemsRef = value);
                yield return new Handle(
                    property.DictionaryValueRef, where + " (values)",
                    value => propertyCaptured.DictionaryValueRef = value);

                foreach (var branch in property.OneOf) {
                    var branchCaptured = branch;

                    yield return new Handle(
                        branch.Ref, where + " (oneOf branch)", value => branchCaptured.Ref = value);
                }
            }
        }

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                var captured = operation;
                var where = operation.HttpMethod + " " + operation.Path;

                yield return new Handle(
                    operation.RequestBodyRef, where + " (request body)",
                    value => captured.RequestBodyRef = value);

                // The success responses were not here, and every pass that walks references needs
                // all of them: a schema renamed by the allocator left a success case pointing at
                // the old name, which is the shape this class exists to stop happening again.
                foreach (var success in operation.SuccessResponses) {
                    var successCaptured = success;
                    var status = where + " (" + success.StatusCode + ")";

                    yield return new Handle(
                        success.Ref, status, value => successCaptured.Ref = value);
                    yield return new Handle(
                        success.ArrayItemsRef, status + " items",
                        value => successCaptured.ArrayItemsRef = value);
                }

                // The flat fields mirror the primary success - the lowest declared 2xx - so they
                // are labelled as it rather than as somewhere of their own. A caller reporting one
                // reference per place reports these once, because the document declares them once.
                // They are still yielded: a pass that rewrites references has to rewrite both
                // copies or the two disagree.
                var primary = where + " (" + operation.SuccessStatusCode + ")";

                yield return new Handle(
                    operation.ResponseRef, primary, value => captured.ResponseRef = value);
                yield return new Handle(
                    operation.ResponseArrayItemsRef, primary + " items",
                    value => captured.ResponseArrayItemsRef = value);

                foreach (var error in operation.ErrorResponses) {
                    var errorCaptured = error;

                    yield return new Handle(
                        error.Ref, where + " (" + error.StatusCode + ")",
                        value => errorCaptured.Ref = value);
                }

                foreach (var parameter in operation.Parameters) {
                    var parameterCaptured = parameter;

                    yield return new Handle(
                        parameter.Ref, where + " parameter '" + parameter.Name + "'",
                        value => parameterCaptured.Ref = value);
                    yield return new Handle(
                        parameter.ArrayItemsRef,
                        where + " parameter '" + parameter.Name + "' (items)",
                        value => parameterCaptured.ArrayItemsRef = value);
                }
            }
        }
    }
}
