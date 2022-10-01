namespace Hardened.SourceGenerator.Templates.Generator;

//public class PackageData
//{
//    public TypeDefinition TypeDefinition { get; set; }

//    public string Extensions { get; set; }

//    public string Token { get; set; } = "{{Token}}";
//}
    
//public class TemplateHelperData
//{
//    public ITypeDefinition TypeDefinition { get; set; }

//    public string HelperName { get; set; }
//}

//public class SyntaxReceiver : ISyntaxReceiver
//{
//    private readonly List<PackageData> _packages = new ();
//    private readonly List<TemplateHelperData> _helpers = new ();

//    public IReadOnlyList<PackageData> Packages => _packages;

//    public IReadOnlyList<TemplateHelperData> Helpers => _helpers;

//    public void OnVisitSyntaxNode(SyntaxNode syntaxNode)
//    {
//        if (syntaxNode is ClassDeclarationSyntax classDeclarationSyntax)
//        {
//            var attributes = syntaxNode.DescendantNodes().OfType<AttributeSyntax>();

//            foreach (var attributeSyntax in attributes)
//            {
//                var attributeName = attributeSyntax.Name.ToString();

//                if (attributeName == "TemplatePackage" || 
//                    attributeName == "TemplatePackageAttribute" ||
//                    attributeName == "Hardened.Templates.Runtime.TemplatePackage")
//                {
//                    var name = classDeclarationSyntax.Identifier.Text;
//                    var namespaceDefinition = classDeclarationSyntax.GetNamespace();

//                    var packageData = new PackageData
//                    {
//                        TypeDefinition = new TypeDefinition(TypeDefinitionEnum.ClassDefinition, namespaceDefinition,
//                            name),
//                        Extensions = "html"
//                    };

//                    _packages.Add(packageData);
//                }
//                else if (attributeName == "TemplateHelper" || 
//                          attributeName == "TemplateHelperAttribute" ||
//                          attributeName == "Hardened.Templates.Runtime.TemplateHelperAttribute")
//                {
//                    var name = classDeclarationSyntax.Identifier.Text;
//                    var namespaceDefinition = classDeclarationSyntax.GetNamespace();

//                    var argument  = attributeSyntax.ArgumentList?.Arguments.FirstOrDefault();
//                    var helperName = argument?.Expression.ToFullString().Trim('"');

//                    if (!string.IsNullOrEmpty(helperName))
//                    {
//                        _helpers.Add(
//                            new TemplateHelperData
//                            {
//                                HelperName = helperName, 
//                                TypeDefinition = TypeDefinition.Get(namespaceDefinition, name)
//                            });
//                    }
//                }
//            }
//        }
//    }
//}