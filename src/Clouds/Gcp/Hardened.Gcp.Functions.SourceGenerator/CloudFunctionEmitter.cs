using CSharpAuthor;
using Hardened.SourceGenerator.Shared;

namespace Hardened.Gcp.Functions.SourceGenerator;

/// <summary>
/// The entry type one application needs in its own assembly, through CSharpAuthor.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the application needs a generator at all.</b> The Functions Framework resolves
/// <c>FUNCTION_TARGET</c> with <c>Assembly.GetType</c> against the assembly its generated
/// <c>Main</c> was compiled into, so the target type has to be in the application's own assembly
/// and no runtime package can supply it. It finds the startup class the same way, from an
/// attribute on that assembly or on that type, and the startup has to name the application - which
/// a runtime package cannot name either. Both are the problem
/// <c>IHardenedFunctionsApplication</c> solves on the Azure worker, and the answer is the same
/// one: the generator writes what only the consumer's assembly can hold.
/// </para>
/// <para>
/// <b>The type is glue and nothing else.</b> Everything that runs per request is
/// <c>CloudFunctionHost</c>, in the runtime package, where it is compiled once and tested once.
/// What is written here is a constructor and a forwarding call, so nothing under measurement is
/// generated per application.
/// </para>
/// </remarks>
internal static class CloudFunctionEmitter
{
    private const string Runtime = "Hardened.Gcp.Functions.Runtime.Hosting";

    private static readonly ITypeDefinition HttpFunction = TypeDefinition.Get(
        "Google.Cloud.Functions.Framework",
        "IHttpFunction"
    );

    private static readonly ITypeDefinition FunctionsStartupAttribute = TypeDefinition.Get(
        "Google.Cloud.Functions.Hosting",
        "FunctionsStartupAttribute"
    );

    private static readonly ITypeDefinition CloudFunctionHost = TypeDefinition.Get(
        Runtime,
        "CloudFunctionHost"
    );

    private static readonly ITypeDefinition HttpContext = TypeDefinition.Get(
        "Microsoft.AspNetCore.Http",
        "HttpContext"
    );

    private static readonly ITypeDefinition Task = TypeDefinition.Get(
        "System.Threading.Tasks",
        "Task"
    );

    /// <summary>
    /// The name the entry type is written under, appended to the application's own.
    /// </summary>
    /// <remarks>
    /// Derived from the application rather than fixed, so an assembly holding two applications
    /// gets two entry types rather than a duplicate definition, and predictable so a deploy script
    /// can write <c>--entry-point</c> by hand:
    /// <code>
    /// gcloud functions deploy orders --gen2 --entry-point MyCompany.Orders.ApplicationCloudFunction
    /// </code>
    /// </remarks>
    public const string Suffix = "CloudFunction";

    /// <summary>The fully qualified name of the entry type for <paramref name="entryPoint"/>.</summary>
    public static string TargetName(EntryPointSelector.Model entryPoint)
    {
        var ns = entryPoint.EntryPointType.Namespace;
        var name = entryPoint.EntryPointType.Name + Suffix;

        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    public static string Emit(EntryPointSelector.Model entryPoint)
    {
        var ns = entryPoint.EntryPointType.Namespace;
        var application = entryPoint.EntryPointType;

        var file = new CSharpFileDefinition(ns);

        var entry = file.AddClass(application.Name + Suffix);

        entry.Modifiers = ComponentModifier.Public | ComponentModifier.Sealed;
        entry.AddBaseType(HttpFunction);

        // The startup on the target type rather than on the assembly. GetStartupTypes queries both,
        // and an attribute here travels with the type it configures - an assembly-level one in a
        // generated file is a second thing to keep in step when an assembly holds two applications.
        //
        // Written as text because the argument is a constructed generic type, and typeof over one
        // is a shape CSharpAuthor has no builder for.
        entry.AddAttribute(
            FunctionsStartupAttribute,
            "typeof(global::" + Runtime + ".HardenedFunctionsStartup<" + Name(application) + ">)"
        );

        var host = entry.AddField(CloudFunctionHost, "_host");

        host.Modifiers = ComponentModifier.Private | ComponentModifier.Readonly;

        var constructor = entry.AddConstructor();

        constructor.Modifiers = ComponentModifier.Public;

        var parameter = constructor.AddParameter(CloudFunctionHost, "host");

        constructor.Assign(parameter).To(host.Instance);

        // The Functions Framework registers the target as scoped and resolves it per request, so
        // this is constructed per request and holds nothing of its own.
        var handle = entry.AddMethod("HandleAsync");

        handle.Modifiers = ComponentModifier.Public;
        handle.SetReturnType(Task);

        var context = handle.AddParameter(HttpContext, "context");

        handle.Return(host.Instance.Invoke("HandleAsync", context));

        return Output(file);
    }

    /// <summary>A type as generated text: fully qualified, the way the Azure emitter writes one.</summary>
    private static string Name(ITypeDefinition type) =>
        string.IsNullOrEmpty(type.Namespace)
            ? type.Name
            : "global::" + type.Namespace + "." + type.Name;

    private static string Output(CSharpFileDefinition file)
    {
        var output = new OutputContext(
            new OutputContextOptions { TypeOutputMode = TypeOutputMode.Global }
        );

        file.WriteOutput(output);

        return output.Output();
    }
}
