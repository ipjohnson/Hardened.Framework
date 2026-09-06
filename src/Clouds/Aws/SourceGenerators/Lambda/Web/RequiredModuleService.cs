using CSharpAuthor;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

/// <summary>
/// Emits a resolve that reports a missing module attribute as a missing module attribute.
/// </summary>
/// <remarks>
/// The bootstrap resolves its transport's services in the application's constructor, so forgetting
/// the module attribute is a container miss on the first line rather than a compile error.
/// <c>GetRequiredService</c> reports that by naming an interface the consumer never wrote down;
/// <c>ApplicationServices.Require</c> names the attribute to add and the class to add it to. See
/// that method for why the check cannot live in the generator.
/// </remarks>
internal static class RequiredModuleService {
    private static readonly ITypeDefinition ApplicationServices =
        TypeDefinition.Get("Hardened.Amz.Shared.Lambda.Runtime", "ApplicationServices");

    /// <param name="service">The service the bootstrap cannot run without.</param>
    /// <param name="providerField">The root provider field, passed as written.</param>
    /// <param name="applicationName">The application class, so the message names the file to edit.</param>
    /// <param name="moduleAttribute">The attribute that registers <paramref name="service"/>.</param>
    public static IOutputComponent Resolve(
        ITypeDefinition service, string providerField, string applicationName, string moduleAttribute) =>
        SyntaxHelpers.InvokeGeneric(
            ApplicationServices,
            "Require",
            new[] { service },
            providerField,
            "\"" + applicationName + "\"",
            "\"" + moduleAttribute + "\"");
}
