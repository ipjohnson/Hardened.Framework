using System.Collections.Generic;
using System.Text.RegularExpressions;
using CSharpAuthor;
using Hardened.Generation.Models;

namespace Hardened.Idl.Validation;

/// <summary>
/// Turns a constraint declared on a path parameter into a route constraint the routing table
/// compiles in, where two operations need it to be told apart.
/// </summary>
/// <remarks>
/// <para>
/// <b>A path pattern is a validation rule, not a route.</b> A value that fails it named a real
/// endpoint with a malformed value, which is what a 400 naming the parameter says, as it says for
/// a body member that fails its <c>pattern</c>. Compiled into the route, the same value answered a
/// bodyless 404 under an operation whose document declares its 404 with a body, and a generated
/// client failed to read it. A Smithy author also expects <c>@pattern</c> on a label to behave as
/// it does on a body member.
/// </para>
/// <para>
/// <b>Kept where it tells two routes apart.</b> Two operations answering the same verb on the same
/// shape of path, such as <c>GET /vans/{vin}</c> and <c>GET /vans/{fleetCode}</c>, match the same
/// URLs without their constraints, so the constraints are what route a value to one or the other.
/// There the pattern stays in the route and a value neither accepts answers 404, because no route
/// matched. Everywhere else the route matches and the generated validator checks the pattern.
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
/// A pattern the registry rejects contributes no constraint. It stays on the validation path, and
/// the registry already records the rejection for the task to report.
/// </para>
/// </remarks>
internal static class RouteConstraintEmitter
{
    /// <summary>
    /// Assigns a route-constraint name to every path parameter carrying a pattern on an operation
    /// that shares its route's shape with another, and emits the constraint methods those names
    /// refer to.
    /// </summary>
    public static void Emit(
        NamespaceDefinition validation,
        ServiceSpecModel model,
        PatternRegistry patterns
    )
    {
        var emitted = new Dictionary<string, string>(System.StringComparer.Ordinal);
        var shared = SharedShapes(model);

        foreach (var service in model.Services)
        {
            foreach (var operation in service.Operations)
            {
                if (!shared.Contains(Shape(operation)))
                {
                    continue;
                }

                foreach (var parameter in operation.Parameters)
                {
                    if (!IsConstrainedPathParameter(parameter))
                    {
                        continue;
                    }

                    // Registers the pattern as a side effect, and answers null for one the
                    // registry rejects. Members is how the member name is read back.
                    if (
                        patterns.AttributeArguments(parameter.Pattern!) == null
                        || !patterns.Members.TryGetValue(parameter.Pattern!, out var member)
                    )
                    {
                        // Rejected by the registry - it does not compile as a regex, and the task
                        // reports it. Leaving the constraint off keeps the route matching and the
                        // validation path answering, which is what happened before this existed.
                        continue;
                    }

                    if (!emitted.TryGetValue(member, out var constraintName))
                    {
                        constraintName = ("spec_" + member).ToLowerInvariant();
                        emitted.Add(member, constraintName);
                    }

                    parameter.RouteConstraint = constraintName;
                }
            }
        }

        if (emitted.Count == 0)
        {
            return;
        }

        var container = validation.AddClass("SpecRouteConstraints");

        container.Modifiers |= ComponentModifier.Static | ComponentModifier.Internal;

        foreach (var pair in emitted)
        {
            var method = container.AddMethod("Is_" + pair.Key);

            method.Modifiers |= ComponentModifier.Static | ComponentModifier.Public;
            method.SetReturnType(typeof(bool));
            method.AddAttribute(
                TypeDefinition.Get("Hardened.Web.Runtime.Attributes", "RouteConstraint"),
                "\"" + pair.Value + "\""
            );

            var value = method.AddParameter(
                TypeDefinition.Get("System", "ReadOnlySpan<char>"),
                "value"
            );

            method.Return(
                new CodeOutputComponent(
                    patterns.ClassName + "." + pair.Key + "().IsMatch(" + value.Name + ")"
                )
            );
        }
    }

    /// <summary>
    /// The shapes more than one operation answers: the verb and the path with every token blanked,
    /// so <c>GET /vans/{vin}</c> and <c>GET /vans/{fleetCode}</c> share <c>GET /vans/{}</c>.
    /// </summary>
    private static HashSet<string> SharedShapes(ServiceSpecModel model)
    {
        var seen = new HashSet<string>(System.StringComparer.Ordinal);
        var shared = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var service in model.Services)
        {
            foreach (var operation in service.Operations)
            {
                var shape = Shape(operation);

                if (!seen.Add(shape))
                {
                    shared.Add(shape);
                }
            }
        }

        return shared;
    }

    /// <summary>
    /// The verb and the path with each token blanked. The path is lower-cased because a module
    /// carrying <c>[CaseInsensitiveRoutes]</c> matches <c>/Vans/{vin}</c> and <c>/vans/{code}</c>
    /// as one route, and the build task cannot see the attribute.
    /// </summary>
    private static string Shape(OperationModel operation) =>
        operation.HttpMethod.ToUpperInvariant()
        + " "
        + Regex.Replace(operation.Path, "\\{[^}]*\\}", "{}").ToLowerInvariant();

    /// <summary>
    /// A path parameter carrying a pattern. Type-based constraints are deliberately left alone for
    /// now: <c>type: integer</c> maps onto the built-in <c>int</c> constraint and is worth doing,
    /// but it changes matching for routes that already work, where a pattern changes matching only
    /// for values that were already reaching a validator and being refused.
    /// </summary>
    private static bool IsConstrainedPathParameter(ParameterModel parameter) =>
        string.Equals(parameter.In, "path", System.StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrEmpty(parameter.Pattern);
}
