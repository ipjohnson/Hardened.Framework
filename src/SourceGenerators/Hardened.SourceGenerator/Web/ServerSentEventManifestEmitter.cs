using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Web.Routing;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// The list of handlers an application answers as <c>text/event-stream</c>, for a host whose
/// framing depends on how it was deployed.
/// </summary>
/// <remarks>
/// <para>
/// Emitted rather than discovered, because a routing table builds its handlers lazily: asking each
/// one at startup would mean constructing every filter chain in the application to read one flag
/// off each. Here the answer is already known, and costs a string per handler.
/// </para>
/// <para>
/// <b>Nothing is emitted when no handler is framed as events</b>, which is almost every
/// application. That is also what keeps this off the checked-in routing fixtures: a table with no
/// event stream generates exactly what it generated before.
/// </para>
/// <para>
/// Its one consumer is the Lambda host, whose response mode is an environment variable rather than
/// a property of the build - so the same assembly serves both modes and only the running
/// application can say the mode and the handlers disagree. Kestrel and ASP.NET Core flush whatever
/// they are given and never ask.
/// </para>
/// </remarks>
internal static class ServerSentEventManifestEmitter {

    public const string ContainerName = "ServerSentEvents";

    private const string HandlersField = "_handlers";

    /// <summary>
    /// The handlers framed as events, as the verb and path an operator reads off a log line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ordered and deduplicated so the emitted list does not move between builds over nothing,
    /// which would dirty the incremental cache and the fixtures with it.
    /// </para>
    /// <para>
    /// Token names only. A route constraint is how the router decides what matches, and one a
    /// specification declared is named after a hash of the pattern - so the warning named
    /// <c>/devices/{deviceId:spec_p_588343bc}</c>, a route the operator cannot find in their own
    /// contract.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> Collect(IReadOnlyList<RequestHandlerModel> handlers) =>
        handlers
            .Where(handler =>
                handler.ResponseInformation.StreamFraming == StreamFramingNames.ServerSentEvents)
            .Select(handler => handler.Name.Method + " " + RouteTemplate.NamesOnly(handler.Name.Path))
            .Distinct()
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();

    public static void Emit(ClassDefinition appClass, IReadOnlyList<string> handlers) {
        if (handlers.Count == 0) {
            return;
        }

        var container = appClass.AddClass(ContainerName);

        container.Modifiers |= ComponentModifier.Private | ComponentModifier.Sealed;
        container.AddBaseType(KnownTypes.Requests.IServerSentEventManifest);
        container.Comment =
            "The handlers this application answers as text/event-stream. Read by a host whose " +
            "framing depends on its deployment; see IServerSentEventManifest.";

        var field = container.AddField(typeof(string[]), HandlersField);

        field.Modifiers |=
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        field.InitializeValue = new CodeOutputComponent(
            "new string[] { " + string.Join(", ", handlers.Select(Quoted)) + " }") { Indented = false };

        var property = container.AddProperty(
            new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                "System.Collections.Generic",
                "IReadOnlyList",
                new[] { TypeDefinition.Get(typeof(string)) }),
            "Handlers");

        property.Modifiers |= ComponentModifier.Public;
        property.Set = null;
        property.Get.LambdaSyntax = true;
        property.Get.AddCode(HandlersField + ";");
    }

    private static string Quoted(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
