using System.Collections.Immutable;
using CSharpAuthor;
using Hardened.SourceGenerator.DependencyInjection;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Templates.Parser;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Templates.Generator;

public static class TemplateIncrementalGenerator
{
    private static readonly StringTokenNodeParser.TokenInfo _tokenInfo = new("{{", "}}");
    private static readonly TemplateClassGenerator _generator =
        new(
            new TemplateParseService(
                new StringTokenNodeParser(new StringTokenNodeCreatorService())),
            new TemplateImplementationGenerator(),
            new TemplateWhiteSpaceCleaner());

    public static void Setup(IncrementalGeneratorInitializationContext initializationContext,
        IncrementalValuesProvider<EntryPointSelector.Model> entryPointProvider, ICollection<string> fileExtension)
    {
        var applicationModelCollection = entryPointProvider.Collect();

        var templateFiles = initializationContext.AdditionalTextsProvider.Where(textFile =>
        {
            var extension = Path.GetExtension(textFile.Path).TrimStart('.');
            return fileExtension.Contains(extension);
        });

        var templateModels = templateFiles.Select(GenerateTemplateModels);

        var templateModelsProvider = templateModels.Combine(applicationModelCollection);

        initializationContext.RegisterSourceOutput(
            templateModelsProvider,
            SourceGeneratorWrapper.Wrap<
                (TemplateModel templateModel, ImmutableArray<EntryPointSelector.Model> applicationModels)
            >(GenerateTemplateSource));

        var templateHandlerProviders = entryPointProvider.Combine(templateModels.Collect());

        initializationContext.RegisterSourceOutput(templateHandlerProviders,
            SourceGeneratorWrapper.Wrap<
                (EntryPointSelector.Model applicationModel, ImmutableArray<TemplateModel> templateModels)>(
                TemplateEntryPointGenerator.Generate));

        var helperSelector = new SyntaxSelector<ClassDeclarationSyntax>(KnownTypes.Templates.TemplateHelperAttribute);

        var templateHelperModels = 
            initializationContext.SyntaxProvider.CreateSyntaxProvider(
            helperSelector.Where,
            TemplateHelperModelGenerator
        ).WithComparer(new TemplateHelperModelComparer());

        var templateHelperProviders = entryPointProvider.Combine(templateHelperModels.Collect());

        initializationContext.RegisterSourceOutput(
            templateHelperProviders,
            SourceGeneratorWrapper.Wrap <
                (EntryPointSelector.Model applicationModel, ImmutableArray<TemplateHelperModel> helperModels)
            >(TemplateHelperGenerator.Generate));
    }

    public class TemplateHelperModelComparer : IEqualityComparer<TemplateHelperModel>
    {
        public bool Equals(TemplateHelperModel x, TemplateHelperModel y)
        {
            if (ReferenceEquals(x, y)) return true;
            if (ReferenceEquals(x, null)) return false;
            if (ReferenceEquals(y, null)) return false;
            return x.Equals(y);
        }

        public int GetHashCode(TemplateHelperModel obj)
        {
            return obj.GetHashCode();
        }
    }

    private static TemplateHelperModel? TemplateHelperModelGenerator(GeneratorSyntaxContext arg1, CancellationToken arg2)
    {
        if (arg1.Node is not ClassDeclarationSyntax classDeclarationSyntax)
        {
            // we should never get here
            throw new Exception("Could not get class declaration");
        }

        var attribute =
            arg1.Node.DescendantNodes().OfType<AttributeSyntax>().First(a => a.Name.ToString().Contains("TemplateHelper"));

        var helperName = attribute.ArgumentList?.Arguments.First().ToString().Trim('"') ?? "";

        return new TemplateHelperModel(helperName,
            TypeDefinition.Get(classDeclarationSyntax.GetNamespace(), classDeclarationSyntax.Identifier.ToString()),
            DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle.Singleton);
    }

    private static void GenerateTemplateSource(SourceProductionContext sourceProductionContext, (TemplateModel templateModel, ImmutableArray<EntryPointSelector.Model> applicationModels) templateData)
    {
        var templateModel = templateData.templateModel;
        var applicationModel = templateData.applicationModels.First();
        templateModel.TemplateDefinitionType = TypeDefinition.Get(
            applicationModel.EntryPointType.Namespace + ".Generated", "Template_" + templateModel.TemplateName);

        var templateSource = _generator.GenerateCSharpFile(templateModel.TemplateActionNodes,
            templateModel.TemplateName, templateModel.TemplateExtension, templateModel.TemplateDefinitionType.Namespace);

        var templateFileName = "Generated." + templateModel.TemplateDefinitionType.Name + ".cs";

        sourceProductionContext.AddSource(templateFileName, templateSource);
    }

    private static TemplateModel GenerateTemplateModels(AdditionalText additionalText, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(additionalText.Path);
        var fileName = Path.GetFileNameWithoutExtension(additionalText.Path);

        var additionalTextString = additionalText.GetText(cancellationToken)?.ToString() ?? "";

        return new TemplateModel(
            fileName,
            extension,
            _generator.ParseCSharpFile(additionalTextString, _tokenInfo));
    }

    public class TemplateHelperModel
    {
        public TemplateHelperModel(string name, ITypeDefinition helper, DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle lifestyle)
        {
            Name = name;
            Helper = helper;
            Lifestyle = lifestyle;
        }

        public string Name { get; }

        public ITypeDefinition Helper { get; }

        public DependencyInjection.DependencyInjectionIncrementalGenerator.ServiceModel.ServiceLifestyle Lifestyle { get; }

        public override bool Equals(object obj)
        {
            if (obj is not TemplateHelperModel templateHelperModel)
            {
                return false;
            }
            return Name.Equals(templateHelperModel.Name) &&
                   Helper.Equals(templateHelperModel.Helper) &&
                   Lifestyle.Equals(templateHelperModel.Lifestyle);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Name.GetHashCode();
                hashCode = (hashCode * 397) ^ Helper.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Lifestyle;
                return hashCode;
            }
        }
    }

    public class TemplateModel
    {
        public TemplateModel(string templateName, string templateExtension, IList<TemplateActionNode> templateActionNodes)
        {
            TemplateName = templateName;
            TemplateExtension = templateExtension;
            TemplateActionNodes = templateActionNodes;
        }

        public ITypeDefinition? TemplateDefinitionType { get; set; }

        public IList<TemplateActionNode> TemplateActionNodes { get; }

        public string TemplateName { get; }

        public string TemplateExtension { get; }

    }
}