using System.Linq;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Function.Lambda.SourceGenerator;

[Generator]
public class StreamingFunctionLambdaSourceGenerator : IIncrementalGenerator {
    /// <summary>
    /// The one attribute that selects response streaming for a function application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two further names were accepted here until 2026-08-27:
    /// <c>StreamingLambdaFunctionApplication</c> and <c>LambdaFunctionApplication</c>. Neither is a
    /// type anywhere in this repository or the framework - they existed only as string literals in
    /// this predicate and as stubs in the generator's own tests, so writing either in an application
    /// was a compile error for an undefined attribute. They read as public API in the source and
    /// were never reachable.
    /// </para>
    /// <para>
    /// The attribute the module generates was <c>[StreamingLambdaFunction]</c> until the same date;
    /// see <c>StreamingLambdaFunctionModule</c> for why it was renamed.
    /// </para>
    /// </remarks>
    private const string StreamingModuleAttribute = "StreamingLambdaFunctionModule";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var isEntryPoint = EntryPointSelector.UsingAttribute();

        // Requires an entry point, which this selector did not. A class with the module attribute
        // and no [HardenedModule] used to have a streaming host generated for it - a Main and an
        // invoke loop on a type that is not an application.
        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, ct) => isEntryPoint(node, ct) && DeclaresStreaming(node),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        LambdaEntryIncrementalGenerator.Setup(context, applicationModel);
        StreamingFunctionBootstrapGenerator.Setup(context, applicationModel);
    }

    /// <summary>
    /// True when the class itself carries the streaming module attribute.
    /// </summary>
    /// <remarks>
    /// A class declaration's attribute lists are its direct children, so <c>ChildNodes()</c> reads
    /// exactly what is written on the class - unlike <c>IsAttributed</c>, which searches the whole
    /// subtree and let an attribute on a member or a nested type select the enclosing application.
    /// The web selector documents the same reasoning, and why this takes a <c>SyntaxNode</c>
    /// without testing its type, at length.
    /// </remarks>
    internal static bool DeclaresStreaming(SyntaxNode node) =>
        node.ChildNodes()
            .OfType<AttributeListSyntax>()
            .SelectMany(list => list.Attributes)
            .Any(attribute => {
                var name = attribute.Name.ToString();
                var simpleName = name.Substring(name.LastIndexOf('.') + 1);

                return simpleName == StreamingModuleAttribute ||
                       simpleName == StreamingModuleAttribute + "Attribute";
            });
}
