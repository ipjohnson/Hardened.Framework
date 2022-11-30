using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Hardened.SourceGenerator.Requests;

public static class InvokeClassGenerator
{
    public static readonly ITypeDefinition GenericParameters = TypeDefinition.Get("", "Parameters");

    public static void GenerateInvokeClass(RequestHandlerModel handlerModel, IConstructContainer constructContainer,
        CancellationToken cancellationToken)
    {
        var invokeClass = constructContainer.AddClass(handlerModel.InvokeHandlerType.Name);

        invokeClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        AssignBaseTypes(handlerModel, invokeClass);
            
        cancellationToken.ThrowIfCancellationRequested();
        HandlerInfoCodeGenerator.Implement(handlerModel, invokeClass);

        CreateConstructor(handlerModel, invokeClass);

        InvokeMethodCodeGenerator.Implement(handlerModel, invokeClass);

        if (handlerModel.RequestParameterInformationList.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            BindRequestParametersMethodGenerator.Implement(handlerModel, invokeClass);
            ParametersClassGenerator.GenerateParametersClass(handlerModel, invokeClass);
        }
    }

    private static void AssignBaseTypes(RequestHandlerModel handlerModel, ClassDefinition invokeClass)
    {
        invokeClass.AddBaseType(
            new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                KnownTypes.Namespace.Hardened.Requests.Runtime.Execution,
                "BaseExecutionHandler",
                new[] { handlerModel.ControllerType }));
    }

    private static void CreateConstructor(RequestHandlerModel handlerModel, ClassDefinition classDefinition)
    {
        var templateName = handlerModel.ResponseInformation.TemplateName;

        IOutputComponent defaultOutput = Null();

        if (!string.IsNullOrEmpty(templateName))
        {
            defaultOutput = Invoke(KnownTypes.Templates.DefaultOutputFuncHelper, "GetTemplateOut",
                "serviceProvider", QuoteString(templateName!));
        }
        else if (!string.IsNullOrEmpty(handlerModel.ResponseInformation.RawResponseContentType))
        {
            var contentType = handlerModel.ResponseInformation.RawResponseContentType!;

            if (!contentType.StartsWith("\""))
            {
                contentType = '"' + contentType + '"';
            }
            
            defaultOutput = Invoke(
                KnownTypes.Requests.RawOutputHelper,
                "OutputFunc",
                contentType);
        }

        if (handlerModel.RequestParameterInformationList.Count == 0)
        {
            if (handlerModel.ResponseInformation.IsAsync)
            {
                CreateAsyncNoParameterConstructor(handlerModel, classDefinition, defaultOutput);
            }
            else
            {
                CreateSyncNoParameterConstructor(handlerModel, classDefinition, defaultOutput);
            }
        }
        else
        {
            if (handlerModel.ResponseInformation.IsAsync)
            {
                CreateAsyncParametersConstructor(handlerModel, classDefinition, defaultOutput);
            }
            else
            {
                CreateSyncParametersConstructor(handlerModel, classDefinition, defaultOutput);
            }
        }
    }
    private static void CreateAsyncNoParameterConstructor(RequestHandlerModel handlerModel, ClassDefinition classDefinition,
        IOutputComponent defaultOutput)
    {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncStandardFilterEmptyParameters",
            new[] { handlerModel.ControllerType },
            "serviceProvider",
            "_handlerInfo",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateAsyncParametersConstructor(RequestHandlerModel handlerModel, ClassDefinition classDefinition, IOutputComponent defaultOutput)
    {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncStandardFilterWithParameters",
            new[] { handlerModel.ControllerType, GenericParameters },
            "serviceProvider",
            "_handlerInfo",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }
        
    private static void CreateSyncNoParameterConstructor(RequestHandlerModel handlerModel, ClassDefinition classDefinition,
        IOutputComponent defaultOutput)
    {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "StandardFilterEmptyParameters",
            new[] { handlerModel.ControllerType },
            "serviceProvider",
            "_handlerInfo",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateSyncParametersConstructor(RequestHandlerModel handlerModel, ClassDefinition classDefinition, IOutputComponent defaultOutput)
    {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "StandardFilterWithParameters",
            new[] { handlerModel.ControllerType, GenericParameters },
            "serviceProvider",
            "_handlerInfo",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }
        
    private static IOutputComponent GenerateFilterEnumerable(RequestHandlerModel handlerModel, ClassDefinition classDefinition)
    {
        var arguments = new List<object>();

        foreach (var filterInformation in handlerModel.Filters)
        {
            var newValue = New((ITypeDefinition)filterInformation.TypeDefinition, filterInformation.Arguments);

            if (!string.IsNullOrEmpty(filterInformation.PropertyAssignment))
            {
                newValue.AddInitValue(filterInformation.PropertyAssignment);
            }

            arguments.Add(newValue);
        }

        return Invoke(KnownTypes.Requests.ExecutionHelper, "GetFilterInfo", arguments.ToArray());
    }
}