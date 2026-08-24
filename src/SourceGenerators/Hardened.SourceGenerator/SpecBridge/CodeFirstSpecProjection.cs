using System.Collections.Generic;
using System.Linq;
using Hardened.Idl.Models;
using Hardened.SourceGenerator.Models.Request;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Describes an attribute-routed application as a <see cref="ServiceSpecModel"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for, and what it is not.</b> The end state is that code-first reads its
/// attributes straight into a spec model, the way the description front-ends read a document into
/// one, and everything downstream of that point is shared. This projects the model code-first
/// already produces instead — which proves the spec model can carry what code-first expresses,
/// without rewriting 300 lines of Roslyn analysis on the hope that it can.
/// </para>
/// <para>
/// Run one way and back — <c>RequestHandlerModel</c> to here and through
/// <see cref="SpecHandlerModelBuilder"/> again — the result must be the model that went in. Where it
/// is not, the spec model is missing something code-first needs, and that is a finding rather than a
/// bug in this file. Once the round trip is identity the analysis can move, and this goes.
/// </para>
/// <para>
/// One service per controller type, because that is the grouping the bridge names types from:
/// it derives a handler class prefix from the service, and a code-first application already has
/// controllers to group by.
/// </para>
/// </remarks>
internal static class CodeFirstSpecProjection {

    /// <summary>
    /// The spec model describing <paramref name="handlers"/>, and the symbols the bridge needs to
    /// rebuild them exactly.
    /// </summary>
    public static (ServiceSpecModel Spec, Dictionary<string, OperationSymbols> Symbols) Project(
        IReadOnlyList<RequestHandlerModel> handlers) {
        var spec = new ServiceSpecModel();
        var symbols = new Dictionary<string, OperationSymbols>(System.StringComparer.Ordinal);

        foreach (var group in handlers.GroupBy(handler => handler.ControllerType.Name)) {
            var service = new ServiceModel {
                Tag = group.Key,
                TypeBaseName = group.Key,
                DispatchHeader = group.First().Name.DispatchHeader
            };

            foreach (var handler in group) {
                var operationId = OperationId(handler);

                service.Operations.Add(new OperationModel {
                    OperationId = operationId,
                    MethodName = handler.HandlerMethod,
                    Path = handler.Name.Path,
                    HttpMethod = handler.Name.Method,
                    DispatchKey = handler.Name.DispatchKey,
                    Tag = handler.Tag,
                    Summary = handler.Summary,
                    Description = handler.Description,
                    IsDeprecated = handler.IsDeprecated,
                    Parameters = handler.RequestParameterInformationList
                        .Where(parameter => parameter.BindingType != ParameterBindType.Body)
                        .Select(Parameter)
                        .ToList()
                });

                symbols[operationId] = new OperationSymbols {
                    ControllerType = handler.ControllerType,
                    InvokeHandlerType = handler.InvokeHandlerType,
                    ResponseInformation = handler.ResponseInformation,
                    RequestBodyType = handler.RequestParameterInformationList
                        .FirstOrDefault(parameter => parameter.BindingType == ParameterBindType.Body)
                        ?.ParameterType,
                    ParameterTypes = handler.RequestParameterInformationList
                        .Where(parameter => parameter.BindingType != ParameterBindType.Body)
                        .ToDictionary(
                            parameter => parameter.Name,
                            parameter => parameter.ParameterType,
                            System.StringComparer.Ordinal),
                    ParameterBindings = handler.RequestParameterInformationList
                        .Where(parameter => parameter.BindingType != ParameterBindType.Body)
                        .ToDictionary(
                            parameter => parameter.Name,
                            parameter => parameter.BindingType,
                            System.StringComparer.Ordinal)
                };
            }

            spec.Services.Add(service);
        }

        return (spec, symbols);
    }

    /// <summary>
    /// Stable and unique across the application, because it is the key the symbols are found under.
    /// </summary>
    /// <remarks>
    /// Type name and method rather than the route: two operations can share a path under different
    /// verbs, and an operation id that collided would silently hand one handler another's types.
    /// </remarks>
    private static string OperationId(RequestHandlerModel handler) =>
        handler.ControllerType.Name + "." + handler.HandlerMethod + "." + handler.Name.Method;

    /// <summary>
    /// The part of a parameter a description could have stated. Its type and how it binds travel in
    /// <see cref="OperationSymbols"/>, because a description cannot name either exactly.
    /// </summary>
    private static ParameterModel Parameter(RequestParameterInformation parameter) =>
        new() {
            Name = parameter.Name,
            In = In(parameter.BindingType),
            IsRequired = parameter.Required,
            Default = parameter.DefaultValue
        };

    private static string In(ParameterBindType bindType) =>
        bindType switch {
            ParameterBindType.Path => "path",
            ParameterBindType.QueryString => "query",
            ParameterBindType.Header => "header",
            ParameterBindType.Cookie => "cookie",

            // Everything else has no place in a wire contract to be described from. The binding is
            // carried in OperationSymbols and this value is never read back.
            _ => "internal"
        };
}
