using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using static CSharpAuthor.SyntaxHelpers;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// The list of handlers an application declares a rate limit on, for a host where the default
/// store does not limit anything.
/// </summary>
/// <remarks>
/// <para>
/// Emitted rather than discovered, for the reason <see cref="ServerSentEventManifestEmitter"/>
/// gives: a routing table builds its handlers lazily. <b>Nothing is emitted when no handler is
/// limited</b>, which keeps this off the checked-in routing fixtures.
/// </para>
/// <para>
/// Its one consumer is the Lambda host. The in-process store counts per execution environment,
/// and Lambda runs as many environments as traffic needs, so the host warns at startup when a
/// limited handler exists and that store is the one registered.
/// </para>
/// </remarks>
internal static class RateLimitManifestEmitter
{
    public const string ContainerName = "RateLimits";

    private const string HandlersField = "_handlers";

    private const string AttributeNamespace = "Hardened.Requests.Runtime.RateLimiting";

    private const string AttributeName = "RateLimitAttribute";

    /// <summary>
    /// The limited handlers, as the verb and path an operator reads off a log line. Every handler,
    /// when the entry point declares a limit for all of them.
    /// </summary>
    public static IReadOnlyList<string> Collect(
        IReadOnlyList<RequestHandlerModel> handlers,
        IReadOnlyList<AttributeModel> entryPointFilters,
        string? basePath
    )
    {
        var everyHandler = entryPointFilters.Any(filter => IsRateLimit(filter.TypeDefinition));

        return handlers
            .Where(handler =>
                everyHandler || handler.Filters.Any(filter => IsRateLimit(filter.TypeDefinition))
            )
            .Select(handler => ServerSentEventManifestEmitter.Name(handler, basePath))
            .Distinct()
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();
    }

    public static void Emit(ClassDefinition appClass, IReadOnlyList<string> handlers)
    {
        if (handlers.Count == 0)
        {
            return;
        }

        var container = appClass.AddClass(ContainerName);

        container.Modifiers |= ComponentModifier.Private | ComponentModifier.Sealed;
        container.AddBaseType(KnownTypes.Requests.IRateLimitManifest);
        container.Comment =
            "The handlers this application declares a rate limit on. Read by a host whose default "
            + "store counts per instance; see IRateLimitManifest.";

        var field = container.AddField(typeof(string[]), HandlersField);

        field.Modifiers |=
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        field.InitializeValue = new CodeOutputComponent(
            "new string[] { " + string.Join(", ", handlers.Select(QuoteString)) + " }"
        )
        {
            Indented = false,
        };

        var property = container.AddProperty(
            new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                "System.Collections.Generic",
                "IReadOnlyList",
                new[] { TypeDefinition.Get(typeof(string)) }
            ),
            "Handlers"
        );

        property.Modifiers |= ComponentModifier.Public;
        property.Set = null;
        property.Get.LambdaSyntax = true;
        property.Get.AddCode(HandlersField + ";");
    }

    private static bool IsRateLimit(ITypeDefinition type) =>
        type.Name == AttributeName && type.Namespace == AttributeNamespace;
}
