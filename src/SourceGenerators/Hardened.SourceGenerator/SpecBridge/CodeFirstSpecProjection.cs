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
                    RequestBodyName = handler.RequestParameterInformationList
                        .FirstOrDefault(parameter => parameter.BindingType == ParameterBindType.Body)
                        ?.Name,
                    ParameterTypes = handler.RequestParameterInformationList
                        .Where(parameter => parameter.BindingType != ParameterBindType.Body)
                        .ToDictionary(
                            WireName,
                            parameter => parameter.ParameterType,
                            System.StringComparer.Ordinal),
                    ParameterOrder = handler.RequestParameterInformationList
                        .OrderBy(parameter => parameter.ParameterIndex)
                        .Select(WireName)
                        .ToList(),
                    ParameterDefaults = handler.RequestParameterInformationList
                        .Where(parameter => parameter.DefaultValue != null)
                        .ToDictionary(WireName, parameter => parameter.DefaultValue!,
                            System.StringComparer.Ordinal),
                    ParameterAttributes = handler.RequestParameterInformationList
                        .Where(parameter => parameter.CustomAttribute != null)
                        .ToDictionary(WireName, parameter => parameter.CustomAttribute!,
                            System.StringComparer.Ordinal),
                    ParameterBindings = handler.RequestParameterInformationList
                        .Where(parameter => parameter.BindingType != ParameterBindType.Body)
                        .ToDictionary(
                            WireName,
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
            // Name is the wire name and MemberName the C# one, which the spec model already
            // separates - a description calls a parameter one thing and the generated member
            // another. Code-first separates them too: [FromHeader("X-Trace-Id")] string traceId
            // binds a header nobody would name traceId. Putting the C# name in Name loses the
            // header, silently, and the generated binder reads the wrong key.
            Name = WireName(parameter),
            MemberNameOverride = parameter.Name,
            In = In(parameter.BindingType),
            IsRequired = parameter.Required,
            Default = parameter.DefaultValue
        };

    /// <summary>
    /// What the parameter is called on the wire, falling back to the member name for the bindings
    /// that have no wire presence at all.
    /// </summary>
    private static string WireName(RequestParameterInformation parameter) =>
        string.IsNullOrEmpty(parameter.BindingName) ? parameter.Name : parameter.BindingName;

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

    /// <summary>
    /// One handler, described and rebuilt through the shared bridge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per handler rather than per application, deliberately. Projecting the whole set would mean
    /// collecting every model before any could be emitted, so editing one handler would rebuild all
    /// of them - a real incrementality regression for a build-time round trip that changes nothing.
    /// The bridge needs only the owning type's name and dispatch header to name what it names, and
    /// both are on the handler.
    /// </para>
    /// <para>
    /// This is the step that puts code-first on the shared path before the analysis moves onto it.
    /// Every existing attribute-routed test then exercises the bridge, rather than only the corpus
    /// the round-trip suite covers.
    /// </para>
    /// </remarks>
    public static RequestHandlerModel RoundTrip(
        RequestHandlerModel handler,
        string modelsNamespace,
        string servicesNamespace,
        string generatedNamespace,
        string validationNamespace) {
        var (spec, symbols) = Project(new[] { handler });

        var rebuilt = SpecHandlerModelBuilder.BuildModels(
            spec, modelsNamespace, servicesNamespace, generatedNamespace, validationNamespace, symbols);

        return rebuilt.Count == 1 ? Carry(handler, rebuilt[0]) : handler;
    }

    /// <summary>
    /// What the spec model has no field for yet, carried across so the round trip is lossless while
    /// the remaining gaps are closed one at a time.
    /// </summary>
    /// <remarks>
    /// Each of these is a description the spec model cannot make. Listing them here rather than
    /// silently reusing the original model is what keeps the size of the remaining work visible.
    /// </remarks>
    private static RequestHandlerModel Carry(RequestHandlerModel from, RequestHandlerModel to) {
        to.ParametersValidator = from.ParametersValidator;
        to.ResponseSchema = from.ResponseSchema;
        to.ResponseSchemas = from.ResponseSchemas;
        to.RequestSchema = from.RequestSchema;

        return from.Filters.Count == 0 ? to : to.WithFilters(from.Filters);
    }
}
