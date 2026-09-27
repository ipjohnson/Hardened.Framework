using System.Linq;
using Hardened.SourceGenerator.Models.Request;
using Microsoft.CodeAnalysis;

namespace Hardened.SourceGenerator.Requests;

/// <summary>
/// Where a handler was written, found in the compilation, for a finding reported there.
/// </summary>
/// <remarks>
/// <para>
/// The handler model carries no location, for the reason <see cref="Shared.LocationInfo"/> gives:
/// a span on it would rebuild every handler below an edit. A finding the model decides is
/// therefore paired with the compilation, and the method is looked up by its controller's name
/// and its own. Only a handler with a finding is paired, so a build with none looks up nothing.
/// </para>
/// <para>
/// Without a location a finding printed as <c>CSC : error</c>, which an editor cannot open at a
/// line.
/// </para>
/// </remarks>
public static class HandlerDeclaration
{
    /// <summary>The handler's method, or null when this compilation declares no such method.</summary>
    public static IMethodSymbol? Find(Compilation compilation, RequestHandlerModel handler)
    {
        var controllerType = handler.ControllerType;
        var name = string.IsNullOrEmpty(controllerType.Namespace)
            ? controllerType.Name
            : controllerType.Namespace + "." + controllerType.Name;

        if (compilation.Assembly.GetTypeByMetadataName(name) is not { } controller)
        {
            return null;
        }

        var methods = controller.GetMembers(handler.HandlerMethod).OfType<IMethodSymbol>().ToList();

        // Overloads are told apart by their parameters' names, which the model keeps in order.
        return methods.FirstOrDefault(method =>
                method
                    .Parameters.Select(parameter => parameter.Name)
                    .SequenceEqual(
                        handler.RequestParameterInformationList.Select(parameter => parameter.Name)
                    )
            ) ?? methods.FirstOrDefault();
    }

    /// <summary>The method's name, or no location when the method was not found.</summary>
    public static Location Of(IMethodSymbol? method) =>
        method?.Locations.FirstOrDefault() ?? Location.None;

    /// <summary>A parameter's name, or the method's when the parameter was not found.</summary>
    public static Location Of(IMethodSymbol? method, string parameter) =>
        method
            ?.Parameters.FirstOrDefault(candidate => candidate.Name == parameter)
            ?.Locations.FirstOrDefault()
        ?? Of(method);
}
