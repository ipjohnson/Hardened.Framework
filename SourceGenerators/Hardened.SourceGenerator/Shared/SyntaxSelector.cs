using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared
{
    public class SyntaxSelector<T> where T : SyntaxNode
    {
        private const string _attributeString = "Attribute";
        private readonly ITypeDefinition _attribute;
        private readonly List<string> _names;

        public SyntaxSelector(ITypeDefinition attribute)
        {
            _attribute = attribute;
            _names = GetAttributeStrings(attribute);
        }

        private List<string> GetAttributeStrings(ITypeDefinition attribute)
        {
            var returnList = new List<string>
            {
                attribute.Name,
                attribute.Namespace + "." + attribute.Name
            };

            if (attribute.Name.EndsWith(_attributeString))
            {
                var simpleName = attribute.Name.Substring(0, attribute.Name.Length - _attributeString.Length);

                returnList.Add(simpleName);
            }

            return returnList;
        }

        public bool Where(SyntaxNode node, CancellationToken token)
        {
            if (node is not T)
            {
                return false;
            }

            var found = node.DescendantNodes()
                .OfType<AttributeSyntax>().Any(a =>
                {
                    var name = a.Name.ToString();
                    return _names.Contains(name);
                });

            return found;
        }
    }
}
