using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.DependencyInjection
{
    public static class DependencyInjectionIncrementalGenerator
    {
        public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
            IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider,
            IReadOnlyList<ITypeDefinition> defaultLibraries)
        {
            var classSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.DI.ExposeAttribute);

            var services = initializationContext.SyntaxProvider.CreateSyntaxProvider(
                classSelector.Where,
                GenerateServiceModel
            ).WithComparer(new ServiceModelComparer());

            var servicesCollection = services.Collect();

            var generator = new DependencyInjectionFileGenerator(defaultLibraries);

            initializationContext.RegisterSourceOutput(
                entryPointProvider.Combine(servicesCollection),
                SourceGeneratorWrapper.Wrap<
                    (EntryPointSelector.Model Left, ImmutableArray<ServiceModel> Right)>(generator.GenerateFile));
        }

        private static ServiceModel GenerateServiceModel(GeneratorSyntaxContext arg1, CancellationToken arg2)
        {
            var classDeclarationSyntax = (ClassDeclarationSyntax)arg1.Node;
            var tryValue = false;
            ITypeDefinition? exposeTypeDef = null;
            var expose = classDeclarationSyntax.DescendantNodes().OfType<AttributeSyntax>()
                .FirstOrDefault(a => a.Name.ToString() == "Expose" || a.Name.ToString() == "ExposeAttribute");

            if (expose != null)
            {
                if (expose.ArgumentList != null)
                {
                    foreach (var argumentSyntax in expose.ArgumentList.Arguments)
                    {
                        var expressionString = argumentSyntax.Expression.ToString();

                        if (argumentSyntax.NameEquals?.ToString().Contains("Try") ?? false)
                        {
                            tryValue = expressionString.Contains("true");
                        }
                        else if (argumentSyntax.Expression is TypeOfExpressionSyntax typeOfExpression)
                        {
                            exposeTypeDef = typeOfExpression.Type.GetTypeDefinition(arg1);
                        }
                    }
                }

                if (exposeTypeDef == null)
                {
                    var exposeType = classDeclarationSyntax.BaseList?.Types.FirstOrDefault();

                    if (exposeType != null)
                    {
                        exposeTypeDef = exposeType.Type.GetTypeDefinition(arg1);
                    }
                }
            }

            if (exposeTypeDef is GenericTypeDefinition genericTypeDefinition)
            {
                exposeTypeDef = genericTypeDefinition.MakeOpenType();
            }

            ITypeDefinition classTypeDefinition;

            if (classDeclarationSyntax.TypeParameterList is { Parameters.Count: > 0 })
            {
                classTypeDefinition =
                    new GenericTypeDefinition(
                        TypeDefinitionEnum.ClassDefinition,
                        classDeclarationSyntax.GetNamespace(),
                        classDeclarationSyntax.Identifier.ToString(),
                        classDeclarationSyntax.TypeParameterList.Parameters.Select(_ => TypeDefinition.Get("", "")).ToArray()
                    );
            }
            else
            {
                classTypeDefinition = TypeDefinition.Get(classDeclarationSyntax.GetNamespace(),
                    classDeclarationSyntax.Identifier.ToString());
            }

            var lifeStyle = ServiceModel.ServiceLifestyle.Transient;

            if (classDeclarationSyntax.IsAttributed("Singleton"))
            {
                lifeStyle = ServiceModel.ServiceLifestyle.Singleton;
            }
            else if (classDeclarationSyntax.IsAttributed("Scoped"))
            {
                lifeStyle = ServiceModel.ServiceLifestyle.Scoped;
            }

            var getEnvironments = GetEnvironments(classDeclarationSyntax);

            return new ServiceModel(exposeTypeDef ?? classTypeDefinition, classTypeDefinition, lifeStyle, tryValue, getEnvironments);
        }

        private static IReadOnlyList<string> GetEnvironments(ClassDeclarationSyntax classDeclarationSyntax)
        {
            var environments = new List<string>();

            foreach (var attributeSyntax in classDeclarationSyntax.GetAttributes("ForEnvironment"))
            {
                var environmentStringAttr = attributeSyntax.ArgumentList?.Arguments.FirstOrDefault();

                if (environmentStringAttr != null)
                {
                    var environmentString = environmentStringAttr.ToString().Trim('"');

                    environments.Add(environmentString);
                }
            }

            return environments;
        }

        public class ServiceModel
        {
            public ServiceModel(
                ITypeDefinition serviceType,
                ITypeDefinition implementationType,
                ServiceLifestyle lifestyle,
                bool @try,
                IReadOnlyList<string> environments)
            {
                ServiceType = serviceType;
                ImplementationType = implementationType;
                Lifestyle = lifestyle;
                Try = @try;
                Environments = environments;
            }

            public enum ServiceLifestyle
            {
                Transient,
                Scoped,
                Singleton
            }

            public ITypeDefinition ServiceType { get; }

            public ITypeDefinition ImplementationType { get; }

            public ServiceLifestyle Lifestyle { get; }

            public bool Try { get; set; }
            public IReadOnlyList<string> Environments { get; }

            public override bool Equals(object obj)
            {
                if (obj is not ServiceModel serviceModel)
                {
                    return false;
                }

                return ServiceType != null && ServiceType.Equals(serviceModel.ServiceType) &&
                       ImplementationType != null && ImplementationType.Equals(serviceModel.ImplementationType) &&
                       Lifestyle.Equals(serviceModel.Lifestyle);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    var hashCode = ServiceType.GetHashCode();
                    hashCode = (hashCode * 397) ^ ImplementationType.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)Lifestyle;
                    return hashCode;
                }
            }
        }
    }
}
