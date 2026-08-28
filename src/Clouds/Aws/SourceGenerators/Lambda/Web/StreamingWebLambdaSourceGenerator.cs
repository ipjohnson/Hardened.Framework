using System.Linq;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.Amz.Web.Lambda.SourceGenerator;

[Generator]
public class StreamingWebLambdaSourceGenerator : IIncrementalGenerator {
    /// <summary>
    /// The one attribute that selects response streaming for a web application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>StreamingLambdaWebApplication</c> was accepted here as an equal alternative until
    /// 2026-08-27. It is a plain attribute that registers nothing, so an application selected by it
    /// got a streaming bootstrap over an empty container and threw on construction. It is
    /// <c>[Obsolete(error: true)]</c> now, and naming it here would only route a build that cannot
    /// succeed into this generator instead of failing at the attribute.
    /// </para>
    /// </remarks>
    private const string StreamingModuleAttribute = "StreamingLambdaWebModule";

    public void Initialize(IncrementalGeneratorInitializationContext context) {
        var isEntryPoint = EntryPointSelector.UsingAttribute();

        var applicationModel = context.SyntaxProvider.CreateSyntaxProvider(
            (node, ct) => isEntryPoint(node, ct) && DeclaresStreaming(node),
            EntryPointSelector.TransformModel(true)
        ).WithComparer(new EntryPointSelector.Comparer());

        StreamingBootstrapGenerator.Setup(context, applicationModel);
    }

    /// <summary>
    /// True when the class itself carries the streaming module attribute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ChildNodes()</c> rather than <c>IsAttributed</c>, which searches
    /// <c>DescendantNodes()</c> - the whole class subtree, members and nested types included. A
    /// <c>[StreamingLambdaWebModule]</c> on a nested class used to switch the enclosing application
    /// to streaming and emit a second bootstrap for the nested one: two streaming hosts from an
    /// attribute written somewhere else entirely. A class declaration's attribute lists are its
    /// direct children, so this reads exactly what is written on the class.
    /// </para>
    /// <para>
    /// It takes a <c>SyntaxNode</c> and asks for attribute lists rather than testing for a
    /// <c>ClassDeclarationSyntax</c> first. Both callers filter through <c>isEntryPoint</c>, which
    /// already requires one, so the type test could never fail - an unreachable branch that
    /// existed only to be defensive about a case its callers make impossible.
    /// </para>
    /// <para>
    /// Still a name comparison and not a symbol - the buffered selector has to reach the opposite
    /// answer from the same syntax without a compilation, so both stay syntactic.
    /// </para>
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
