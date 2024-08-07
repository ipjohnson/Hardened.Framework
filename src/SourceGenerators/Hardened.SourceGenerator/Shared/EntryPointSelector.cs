﻿using CSharpAuthor;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Hardened.SourceGenerator.Shared;

public static class EntryPointSelector {
    public class Model {
        public ITypeDefinition EntryPointType { get; set; } = default!;

        public IReadOnlyList<AttributeModel> AttributeModels { get; set; } = default!;

        public bool RootEntryPoint { get; set; }

        public IReadOnlyList<HardenedMethodDefinition> MethodDefinitions { get; set; } = default!;
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
                   CompareMethodDefinitions(x, y);
        }

        private bool CompareAttributes(Model x, Model y) {
            if (x.MethodDefinitions.Count != y.MethodDefinitions.Count) {
                return false;
            }

            for (var i = 0; i < x.MethodDefinitions.Count; i++) {
                if (!x.MethodDefinitions[i].Equals(y.MethodDefinitions[i])) {
                    return false;
                }
            }

            return true;
        }

        private bool CompareMethodDefinitions(Model x, Model y) {
            if (x.MethodDefinitions.Count != y.MethodDefinitions.Count) {
                return false;
            }

            for (var i = 0; i < x.MethodDefinitions.Count; i++) {
                var xDefinition = x.MethodDefinitions[i];
                var yDefinition = y.MethodDefinitions[i];

                if (!xDefinition.Equals(yDefinition)) {
                    return false;
                }
            }

            return true;
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
                AttributeModels = attributes
            };
        };
    }

    private class ModelComparer : IComparer<Model> {
        public int Compare(Model x, Model y) {
            if (ReferenceEquals(x, y)) return 0;
            if (ReferenceEquals(null, y)) return 1;
            if (ReferenceEquals(null, x)) return -1;
            return x.RootEntryPoint.CompareTo(y.RootEntryPoint);
        }
    }
}