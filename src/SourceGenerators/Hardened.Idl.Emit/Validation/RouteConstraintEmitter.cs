using System.Collections.Generic;
using CSharpAuthor;
using Hardened.Generation.Models;

namespace Hardened.Idl.Validation;

/// <summary>
/// Turns a constraint declared on a path parameter into a route constraint the routing table
/// compiles in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a path parameter is different from every other kind.</b> A constraint on a path segment
/// narrows which URLs name a resource, so violating it means the route did not match and the answer
/// is 404. That is the same reasoning already settled for an empty token: <c>/pets/</c> against
/// <c>/pets/{petId}</c> answers 404 rather than telling a client it addressed a real endpoint
/// incorrectly about a URL addressing no endpoint at all. A constraint on a query, header or body
/// parameter is a judgement about a request that did name a resource; it stays on the validation
/// path and answers 400.
/// </para>
/// <para>
/// <b>Nothing new is invented to carry it.</b> The emitted method uses
/// <c>[RouteConstraint]</c> — the same public mechanism a code-first application uses to declare
/// <c>[Get("/books/{code:isbn}")]</c> — so the routing table emits a direct static call and needs to
/// know nothing about descriptions. The regex behind it comes from <see cref="PatternRegistry"/>,
/// which already writes <c>[GeneratedRegex]</c> members for validation: a task writes ordinary
/// source into <c>@(Compile)</c>, so the regex generator sees it and an AOT publish pays 33 KB
/// rather than the 448 KB a runtime-constructed <c>Regex</c> costs.
/// </para>
/// <para>
/// A pattern the registry rejects contributes no constraint. It stays on the validation path, which
/// is the behaviour before this existed, and the registry already records the rejection for the task
/// to report.
/// </para>
/// </remarks>
internal static class RouteConstraintEmitter {

    /// <summary>
    /// Assigns a route-constraint name to every path parameter carrying a pattern, and emits the
    /// constraint methods those names refer to.
    /// </summary>
    public static void Emit(
        NamespaceDefinition validation,
        ServiceSpecModel model,
        PatternRegistry patterns) {
        var emitted = new Dictionary<string, string>(System.StringComparer.Ordinal);

        foreach (var service in model.Services) {
            foreach (var operation in service.Operations) {
                foreach (var parameter in operation.Parameters) {
                    if (!IsConstrainedPathParameter(parameter)) {
                        continue;
                    }

                    // Registers the pattern as a side effect, and answers null for one the
                    // registry rejects. Members is how the member name is read back.
                    if (patterns.AttributeArguments(parameter.Pattern!) == null ||
                        !patterns.Members.TryGetValue(parameter.Pattern!, out var member)) {
                        // Rejected by the registry - it does not compile as a regex, and the task
                        // reports it. Leaving the constraint off keeps the route matching and the
                        // validation path answering, which is what happened before this existed.
                        continue;
                    }

                    if (!emitted.TryGetValue(member, out var constraintName)) {
                        constraintName = ("spec_" + member).ToLowerInvariant();
                        emitted.Add(member, constraintName);
                    }

                    parameter.RouteConstraint = constraintName;
                }
            }
        }

        if (emitted.Count == 0) {
            return;
        }

        var container = validation.AddClass("SpecRouteConstraints");

        container.Modifiers |= ComponentModifier.Static | ComponentModifier.Internal;

        foreach (var pair in emitted) {
            var method = container.AddMethod("Is_" + pair.Key);

            method.Modifiers |= ComponentModifier.Static | ComponentModifier.Public;
            method.SetReturnType(typeof(bool));
            method.AddAttribute(
                TypeDefinition.Get("Hardened.Web.Runtime.Attributes", "RouteConstraint"),
                "\"" + pair.Value + "\"");

            var value = method.AddParameter(
                TypeDefinition.Get("System", "ReadOnlySpan<char>"), "value");

            method.Return(new CodeOutputComponent(
                patterns.ClassName + "." + pair.Key + "().IsMatch(" + value.Name + ")"));
        }
    }

    /// <summary>
    /// A path parameter carrying a pattern. Type-based constraints are deliberately left alone for
    /// now: <c>type: integer</c> maps onto the built-in <c>int</c> constraint and is worth doing,
    /// but it changes matching for routes that already work, where a pattern changes matching only
    /// for values that were already reaching a validator and being refused.
    /// </summary>
    private static bool IsConstrainedPathParameter(ParameterModel parameter) =>
        string.Equals(parameter.In, "path", System.StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(parameter.Pattern);
}
