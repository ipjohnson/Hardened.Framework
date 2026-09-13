using System.Collections.Generic;
using System.Linq;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Web.Routing;

namespace Hardened.SourceGenerator.Web;

/// <summary>
/// Every handler this entry point generated, so a path computed at run time can be pointed at one.
/// </summary>
/// <remarks>
/// <para>
/// The split the whole feature rests on: the route is data and the handler is compiled code. What
/// is emitted here is a factory per handler, keyed by the controller method it came from, plus the
/// two build-time facts a runtime table cannot read anywhere else - whether this entry point
/// matches case-insensitively, and what its base path is - and the <c>[RouteConstraint]</c> methods
/// the application declared, handed over as delegates so a template that only exists at run time
/// can still name one.
/// </para>
/// <para>
/// <b>Nothing is emitted unless the compilation declares an <c>IRouteRegistration</c>.</b> An
/// application that registers no routes at run time generates exactly what it generated before this
/// existed, which is also what keeps this off the checked-in routing fixtures.
/// </para>
/// <para>
/// The factory is a static lambda calling a constructor, so the handler class stays rooted for the
/// trimmer by ordinary reference and nothing in the path uses reflection.
/// </para>
/// </remarks>
internal static class RouteHandlerCatalogEmitter
{
    public const string ContainerName = "RegisteredRouteHandlers";

    private const string HandlersField = "_handlers";

    private const string ConstraintsField = "_constraints";

    private static readonly ITypeDefinition GeneratedRouteHandler = TypeDefinition.Get(
        KnownTypes.Namespace.Hardened.Web.RuntimeRouting,
        "GeneratedRouteHandler"
    );

    private static readonly ITypeDefinition RouteConstraintTest = TypeDefinition.Get(
        KnownTypes.Namespace.Hardened.Web.RuntimeRouting,
        "RouteConstraintTest"
    );

    private static readonly ITypeDefinition Catalog = TypeDefinition.Get(
        TypeDefinitionEnum.InterfaceDefinition,
        KnownTypes.Namespace.Hardened.Web.RuntimeRouting,
        "IGeneratedRouteHandlerCatalog"
    );

    public static ITypeDefinition Type(EntryPointSelector.Model appModel) =>
        TypeDefinition.Get(
            appModel.EntryPointType.Namespace,
            appModel.EntryPointType.Name + "." + ContainerName
        );

    public static void Emit(
        ClassDefinition appClass,
        IReadOnlyList<RequestHandlerModel> handlers,
        IReadOnlyList<RouteConstraintModel> constraints,
        bool caseInsensitive,
        string basePath
    )
    {
        var container = appClass.AddClass(ContainerName);

        container.Modifiers |= ComponentModifier.Private | ComponentModifier.Sealed;
        container.AddBaseType(Catalog);
        container.Comment =
            "Every handler this entry point generated, so a route registered at startup can point "
            + "at one. See IGeneratedRouteHandlerCatalog.";

        var handlerField = container.AddField(GeneratedRouteHandler.MakeArray(), HandlersField);

        handlerField.Modifiers |=
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        handlerField.InitializeValue = new CodeOutputComponent(
            "new global::Hardened.Web.Runtime.Routing.GeneratedRouteHandler[] { "
                + string.Join(", ", handlers.Select(Entry))
                + " }"
        )
        {
            Indented = false,
        };

        var constraintField = container.AddField(ConstraintDictionary(), ConstraintsField);

        constraintField.Modifiers |=
            ComponentModifier.Private | ComponentModifier.Static | ComponentModifier.Readonly;
        constraintField.InitializeValue = new CodeOutputComponent(ConstraintMap(constraints))
        {
            Indented = false,
        };

        Property(
            container,
            new GenericTypeDefinition(
                TypeDefinitionEnum.InterfaceDefinition,
                "System.Collections.Generic",
                "IReadOnlyList",
                new[] { GeneratedRouteHandler }
            ),
            "Handlers",
            HandlersField
        );

        Property(
            container,
            TypeDefinition.Get(typeof(bool)),
            "CaseInsensitiveRoutes",
            caseInsensitive ? "true" : "false"
        );

        Property(container, TypeDefinition.Get(typeof(string)), "BasePath", Quoted(basePath));

        Property(container, ConstraintDictionary(), "Constraints", ConstraintsField);
    }

    private static ITypeDefinition ConstraintDictionary() =>
        new GenericTypeDefinition(
            TypeDefinitionEnum.InterfaceDefinition,
            "System.Collections.Generic",
            "IReadOnlyDictionary",
            new[] { TypeDefinition.Get(typeof(string)), RouteConstraintTest }
        );

    private static void Property(
        ClassDefinition container,
        ITypeDefinition type,
        string name,
        string value
    )
    {
        var property = container.AddProperty(type, name);

        property.Modifiers |= ComponentModifier.Public;
        property.Set = null;
        property.Get.LambdaSyntax = true;
        property.Get.AddCode(value + ";");
    }

    /// <remarks>
    /// The declared route rather than the composed one. The registry reads it for its token names,
    /// and the base path contributes none.
    /// </remarks>
    private static string Entry(RequestHandlerModel handler) =>
        "new global::Hardened.Web.Runtime.Routing.GeneratedRouteHandler("
        + "typeof("
        + Global(handler.ControllerType)
        + "), "
        + Quoted(handler.HandlerMethod)
        + ", "
        + Quoted(handler.Name.Method)
        + ", "
        + Quoted(handler.Name.Path)
        + ", "
        + "static (serviceProvider, routePath) => new "
        + Global(handler.InvokeHandlerType)
        + "(serviceProvider, routePath))";

    private static string Global(ITypeDefinition type) =>
        "global::" + type.Namespace + "." + type.Name;

    private static string ConstraintMap(IReadOnlyList<RouteConstraintModel> constraints)
    {
        var declared = constraints.Where(constraint => constraint.SignatureIsValid).ToList();

        if (declared.Count == 0)
        {
            return "new global::System.Collections.Generic.Dictionary<string, global::Hardened.Web.Runtime.Routing.RouteConstraintTest>()";
        }

        return "new global::System.Collections.Generic.Dictionary<string, global::Hardened.Web.Runtime.Routing.RouteConstraintTest> { "
            + string.Join(
                ", ",
                declared.Select(constraint =>
                    "{ " + Quoted(constraint.Name) + ", " + constraint.Call + " }"
                )
            )
            + " }";
    }

    private static string Quoted(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
