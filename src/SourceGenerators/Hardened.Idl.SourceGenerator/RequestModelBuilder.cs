using CSharpAuthor;
using Hardened.Generation.Models;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.Idl;
using Hardened.Generation;

namespace Hardened.Idl.SourceGenerator;

/// <summary>
/// The description reader's entry point onto <see cref="SpecHandlerModelBuilder"/>, plus the
/// enrichment that only a described application has.
/// </summary>
/// <remarks>
/// <c>HandlerInfo</c> is internal to this assembly - it describes a <c>[Handler]</c> class the
/// build task found - so the two methods below cannot move to the spine with the rest. The bridge
/// itself has no idea they exist.
/// </remarks>
internal static class RequestModelBuilder {

    public static List<RequestHandlerModel> BuildModels(
        ServiceSpecModel spec,
        string modelsNamespace,
        string servicesNamespace,
        string generatedNamespace,
        string validationNamespace) =>
        SpecHandlerModelBuilder.BuildModels(
            spec, modelsNamespace, servicesNamespace, generatedNamespace, validationNamespace);

    internal static string DeriveControllerName(string interfaceName) =>
        SpecHandlerModelBuilder.DeriveControllerName(interfaceName);

    public static List<RequestHandlerModel> EnrichWithHandlerFilters(
        List<RequestHandlerModel> models,
        IReadOnlyList<HandlerInfo> handlerInfos) {
        if (handlerInfos.Count == 0) return models;

        var result = new List<RequestHandlerModel>(models.Count);

        foreach (var model in models) {
            var handlerInfo = FindHandlerInfo(model, handlerInfos);
            if (handlerInfo != null) {
                var filters = new List<AttributeModel>(model.Filters);
                filters.AddRange(handlerInfo.ClassFilters);

                // Find method-level filters matching this handler's method
                var responseInformation = model.ResponseInformation;

                foreach (var methodFilter in handlerInfo.MethodFilters) {
                    if (string.Equals(methodFilter.MethodName, model.HandlerMethod,
                            StringComparison.Ordinal)) {
                        filters.AddRange(methodFilter.Filters);

                        // Which view renders a response is how the operation is fulfilled, not part
                        // of the contract it publishes - so it is read from the implementation and
                        // there is nothing in the document to override or be overridden by.
                        if (methodFilter.OutputType != null) {
                            responseInformation =
                                responseInformation with { OutputType = methodFilter.OutputType };
                        }

                        break;
                    }
                }

                // Through WithFilters, which carries every settable member, rather than a second
                // hand-rolled copy. This used to restate the members one by one, and each field
                // added to the model was silently dropped here until someone noticed - the tag's
                // description was the latest. One copy site is the fix, not a longer list.
                result.Add(model.WithFilters(filters, responseInformation));
            } else {
                result.Add(model);
            }
        }

        return result;
    }
    /// <summary>
    /// The handler implementing this operation's service, wherever it sits in the base list.
    /// </summary>
    /// <remarks>
    /// This compared against the first base-list entry alone, so a handler declaring a base class -
    /// which C# requires to come first - matched nothing and lost every filter and
    /// <c>[Output&lt;T&gt;]</c> written on it, silently.
    /// </remarks>
    private static HandlerInfo? FindHandlerInfo(
        RequestHandlerModel model,
        IReadOnlyList<HandlerInfo> handlerInfos) {
        foreach (var info in handlerInfos) {
            foreach (var candidate in info.InterfaceCandidates) {
                if (candidate.Name == model.ControllerType.Name) {
                    return info;
                }
            }
        }

        return null;
    }
}
