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

            public IReadOnlyCollection<HardenedMethodDefinition> MethodDefinitions { get; set; }
        }

        public static Func<SyntaxNode, CancellationToken, bool> UsingAttribute(string libraryAttribute)
        {
            return (node, _) => node is ClassDeclarationSyntax && node.IsAttributed(libraryAttribute);
        }

        public static Model TransformModel(GeneratorSyntaxContext syntaxContext, CancellationToken cancellationToken)
        {
            var methods = syntaxContext.Node.DescendantNodes().OfType<MethodDeclarationSyntax>();
            
            return new Model
            {
                ApplicationType = ((ClassDeclarationSyntax)syntaxContext.Node).GetTypeDefinition(),
                MethodDefinitions = GenerateMethodDefinitions(syntaxContext, methods)
            };
        }

        private static IReadOnlyCollection<HardenedMethodDefinition> GenerateMethodDefinitions(
            GeneratorSyntaxContext generatorSyntaxContext, 
            IEnumerable<MethodDeclarationSyntax> methods)
        {
            var returnList = new List<HardenedMethodDefinition>();
            
            foreach (var method in methods)
            {
                returnList.Add(method.GetMethodDefinition(generatorSyntaxContext));
            }

            return returnList;
        }
    }
}
