using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Shared;

public static class EntryPointSelector {
    public class Model {
        public ITypeDefinition EntryPointType { get; set; } = default!;

        public IReadOnlyList<AttributeModel> AttributeModels { get; set; } = default!;

        public bool RootEntryPoint { get; set; }

        public IReadOnlyList<HardenedMethodDefinition> MethodDefinitions { get; set; } = default!;

        public IReadOnlyList<HardenedPropertyDefinition>? PropertyDefinitions { get; set; }
    }

    public class Comparer : IEqualityComparer<Model> {
        public bool Equals(Model x, Model y) {
            var equalsValue = InternalEquals(x, y);

            return equalsValue;
        }

        private bool InternalEquals(Model x, Model y) {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;

            return x.EntryPointType.Equals(y.EntryPointType) &&
                   x.RootEntryPoint == y.RootEntryPoint &&
                   CompareAttributes(x, y) &&
                   CompareMethodDefinitions(x, y) &&
                   CompareProperties(x, y);
        }

        private bool CompareProperties(Model x, Model y) {
            if (x.PropertyDefinitions == null) {
                if (y.PropertyDefinitions == null) {
                    return true;
                }
                return false;
            }

            if (y.PropertyDefinitions == null) {
                return false;
            }

            return x.PropertyDefinitions.SequenceEqual(y.PropertyDefinitions);
        }

        private bool CompareAttributes(Model x, Model y) {
            if (x.AttributeModels == null) {
                if (y.AttributeModels == null) {
                    return true;
                }
                return false;
            }

            if (y.AttributeModels == null) {
                return false;
            }

            return x.AttributeModels.SequenceEqual(y.AttributeModels);
        }

        private bool CompareMethodDefinitions(Model x, Model y) {
            if (x.MethodDefinitions == null) {
                if (y.MethodDefinitions == null) {
                    return true;
                }
                return false;
            }

            if (y.MethodDefinitions == null) {
                return false;
            }

            return x.MethodDefinitions.SequenceEqual(y.MethodDefinitions);
        }

        public int GetHashCode(Model obj) {
            unchecked {
                var hashCode = obj.EntryPointType.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.RootEntryPoint.GetHashCode();
                hashCode = (hashCode * 397) ^ obj.MethodDefinitions.GetHashCode();
                return hashCode;
            }
        }
    }

    public static Func<SyntaxNode, CancellationToken, bool> UsingAttribute() {
        return (node, _) => node is ClassDeclarationSyntax && node.IsAttributed("HardenedModule");
    }

    private static IReadOnlyList<HardenedMethodDefinition> GenerateMethodDefinitions(
        GeneratorSyntaxContext generatorSyntaxContext,
        IEnumerable<MethodDeclarationSyntax> methods) {
        var returnList = new List<HardenedMethodDefinition>();

        foreach (var method in methods) {
            returnList.Add(method.GetMethodDefinition(generatorSyntaxContext));
        }

        return returnList;
    }

    public static Func<GeneratorSyntaxContext, CancellationToken, Model> TransformModel(bool rootEntryPoint) {
        return (syntaxContext, token) => {
            var methods = syntaxContext.Node.DescendantNodes().OfType<MethodDeclarationSyntax>();

            IReadOnlyList<AttributeModel> attributes = Array.Empty<AttributeModel>();

            if (syntaxContext.Node is ClassDeclarationSyntax classDeclarationSyntax) {
                attributes = AttributeModelHelper
                    .GetAttributes(syntaxContext, classDeclarationSyntax.AttributeLists, token)
                    .ToList();
            }

            return new Model {
                EntryPointType = ((ClassDeclarationSyntax)syntaxContext.Node).GetTypeDefinition(),
                MethodDefinitions = GenerateMethodDefinitions(syntaxContext, methods),
                RootEntryPoint = rootEntryPoint,
                AttributeModels = attributes,
                PropertyDefinitions = GeneratePropertyDefinitions(syntaxContext)
            };
        };
    }

    private static IReadOnlyList<HardenedPropertyDefinition>? GeneratePropertyDefinitions(GeneratorSyntaxContext syntaxContext) {
        var propertyDeclarationSyntaxes =
            syntaxContext.Node.DescendantNodes().OfType<PropertyDeclarationSyntax>();

        var properties = new List<HardenedPropertyDefinition>();

        foreach (var propertyDeclaration in propertyDeclarationSyntaxes) {
            var publicValue = false;
            var staticValue = false;

            foreach (var modifier in propertyDeclaration.Modifiers) {
                if (modifier.Text == "public") {
                    publicValue = true;
                }
                else if (modifier.Text == "static") {
                    staticValue = true;
                }
            }

            var symbol = syntaxContext.SemanticModel.GetDeclaredSymbol(propertyDeclaration);
            
            if (publicValue &&
                !staticValue &&
                symbol is { IsReadOnly: false }) {
                var propertyType =
                    propertyDeclaration.Type.GetTypeDefinition(syntaxContext);

                if (propertyType == null) {
                    throw new Exception($"Property {propertyDeclaration.Identifier.ValueText} has no type");
                }
                
                properties.Add(new HardenedPropertyDefinition(
                    propertyDeclaration.Identifier.Text,
                    propertyType
                ));
            }
        }

        return properties;
    }
}