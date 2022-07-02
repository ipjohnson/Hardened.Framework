using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared
{
    public static class ApplicationSelector
    {
        public class Model
        {
            public ITypeDefinition ApplicationType { get; set; }
        }

        public static Func<SyntaxNode, CancellationToken, bool> UsingAttribute(string libraryAttribute)
        {
            return (node, _) => node is ClassDeclarationSyntax && node.IsAttributed(libraryAttribute);
        }

        public static Model TransformModel(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
        {
            return new Model { ApplicationType = ((ClassDeclarationSyntax)syntaxContext.Node).GetTypeDefinition() };
        }
    }
}
