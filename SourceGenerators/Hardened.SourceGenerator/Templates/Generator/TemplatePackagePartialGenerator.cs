using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Shared;

namespace Hardened.SourceGenerator.Templates.Generator
{
    //public class TemplatePackagePartialGenerator
    //{
    //    private const string _providerClassName = "TemplateProvider";
    //    private const string _templateHelperClassName = "TemplateHelperProvider";

    //    public string WritePackagePartialClass(TypeDefinition partialClass,
    //        IReadOnlyList<Tuple<string, TypeDefinition>> templates,
    //        IReadOnlyList<TemplateHelperData> syntaxReceiverHelpers)
    //    {
    //        var csharpFile = new CSharpFileDefinition(partialClass.Namespace);

    //        var classDefinition = csharpFile.AddClass(partialClass.Name);

    //        classDefinition.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

    //        var helperProvider = CreateHelperProviderClass(classDefinition, syntaxReceiverHelpers);

    //        var templateProviderClass = CreateTemplateProviderClass(classDefinition, templates);

    //        CreateRegisterMethod(classDefinition, templateProviderClass, helperProvider);

    //        var outputContext = new OutputContext();

    //        csharpFile.WriteOutput(outputContext);

    //        return outputContext.Output();
    //    }

    //    private ClassDefinition CreateHelperProviderClass(ClassDefinition classDefinition, IReadOnlyList<TemplateHelperData> syntaxReceiverHelpers)
    //    {
    //        if (syntaxReceiverHelpers.Count > 0)
    //        {
    //            var helperClassDefinition = classDefinition.AddClass(_templateHelperClassName);

    //            helperClassDefinition.Modifiers |= ComponentModifier.Private;

    //            helperClassDefinition.AddBaseType(KnownTypes.Templates.ITemplateHelperProvider);

    //            CreateHelperMethod(helperClassDefinition, syntaxReceiverHelpers);

    //            return classDefinition;
    //        }

    //        return null;
    //    }

    //    private void CreateHelperMethod(ClassDefinition helperClass, IReadOnlyList<TemplateHelperData> syntaxReceiverHelpers)
    //    {
    //        var factoryMethod = helperClass.AddMethod("GetTemplateHelperFactory");

    //        factoryMethod.Modifiers |= ComponentModifier.Override;
    //        factoryMethod.SetReturnType(KnownTypes.Templates.TemplateHelperFactory);
    //        factoryMethod.AddParameter(typeof(string), "mustacheToken");

    //        var block = factoryMethod.Switch("mustacheToken");

    //        foreach (var helper in syntaxReceiverHelpers)
    //        {
    //            var fieldName = "_helper_" + helper.HelperName.Replace('.','_').Replace('-','_');

    //            var field = helperClass.AddField(KnownTypes.Templates.TemplateHelperFactory, fieldName);

    //            var caseBlock = block.AddCase(QuoteString(helper.HelperName));

    //            var newInstance = New(helper.TypeDefinition);

    //            var invokeFactory = Invoke(KnownTypes.Templates.DefaultHelpers, "CreateTemplateHelperFactory",
    //                newInstance);

    //            caseBlock.Return(NullCoalesceEqual(field.Instance, invokeFactory));
    //        }
            
    //        factoryMethod.Return(Null());
    //    }

    //    private void CreateRegisterMethod(ClassDefinition classDefinition, 
    //        ClassDefinition templateProviderClass,
    //        ClassDefinition helperProvider)
    //    {
    //        var method = classDefinition.AddMethod("Register");
            
    //        method.AddParameter(KnownTypes.DI.IServiceCollection, "serviceCollection");

    //        //var registrationType = KnownTypeDefinitions.DependencyInjectionRegistration;

    //        //method.AddStatement("{arg1}.RegisterKnownServices(serviceCollection);", registrationType);

    //        //if (templateProviderClass != null)
    //        //{
    //        //    method.AddStatement("serviceCollection.AddSingleton<{arg1},{arg2}>();",
    //        //        KnownTypeDefinitions.ITemplateExecutionHandlerProvider,
    //        //        TypeDefinition.Get("", _providerClassName)
    //        //    );
    //        //}

    //        //if (helperProvider != null)
    //        //{
    //        //    method.AddStatement(
    //        //        "serviceCollection.AddSingleton<{arg1},{arg2}>();",
    //        //        KnownTypeDefinitions.ITemplateHelperProvider,
    //        //        TypeDefinition.Get("", _templateHelperClassName)
    //        //        );
    //        //}

    //        //method.Modifiers = ComponentModifier.Public | ComponentModifier.Static;
    //    }

    //    private ClassDefinition? CreateTemplateProviderClass(ClassDefinition classDefinition,
    //        IReadOnlyList<Tuple<string, TypeDefinition>> templates)
    //    {
    //        if (templates.Count > 0)
    //        {
    //            var providerClass = classDefinition.AddClass(_providerClassName);

    //            providerClass.Modifiers |= ComponentModifier.Private;

    //            providerClass.AddBaseType(KnownTypes.Templates.ITemplateExecutionHandlerProvider);

    //            var servicesField = providerClass.AddField(KnownTypes.Templates.IInternalTemplateServices, "_services");

    //            var constructor = providerClass.AddConstructor();

    //            var servicesParameter = constructor.AddParameter(KnownTypes.Templates.IInternalTemplateServices, "services");

    //            constructor.Assign(servicesParameter).To(servicesField.Instance);

    //            CreateTemplateProviderMethod(providerClass, templates);

    //            return providerClass;
    //        }

    //        return null;
    //    }

    //    private void CreateTemplateProviderMethod(ClassDefinition providerClass, IReadOnlyList<Tuple<string, TypeDefinition>> templates)
    //    {
    //        var method = providerClass.AddMethod("GetTemplateExecutionHandler");

    //        method.SetReturnType(KnownTypes.Templates.ITemplateExecutionHandler);

    //        var templateExecutionService = method.AddParameter(KnownTypes.Templates.TemplateExecutionService, "templateExecutionService");
    //        var templateName = method.AddParameter(typeof(string), "templateName");

    //        var switchBlock = method.Switch(templateName);

    //        foreach (var templateTuple in templates)
    //        {
    //            var fieldName = "_handler_" + GetSafeName(templateTuple.Item1);

    //            var handlerField = providerClass.AddField(KnownTypes.Templates.ITemplateExecutionHandler, fieldName);

    //            var caseBlock = switchBlock.AddCase(QuoteString(templateTuple.Item1));

    //            var newTemplateHandler = New(templateTuple.Item2, templateExecutionService, "_services");

    //            caseBlock.Return(NullCoalesceEqual(handlerField.Instance, newTemplateHandler));
    //        }

    //        switchBlock.AddDefault().Return(Null());
    //    }

    //    private string GetSafeName(string name)
    //    {
    //        return name.Replace('.', '_').Replace('-','_');
    //    }
    //}
}
