using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Hardened.SourceGenerator.Requests;

public static class InvokeClassGenerator {
    public static readonly ITypeDefinition GenericParameters = TypeDefinition.Get("", "Parameters");

    public static void GenerateInvokeClass(RequestHandlerModel handlerModel, IConstructContainer constructContainer,
        CancellationToken cancellationToken, bool excludeFromCoverage = false) {
        var invokeClass = constructContainer.AddClass(handlerModel.InvokeHandlerType.Name);

        invokeClass.Modifiers = ComponentModifier.Public | ComponentModifier.Partial;

        if (excludeFromCoverage) {
            invokeClass.AddAttribute(
                TypeDefinition.Get("System.Diagnostics.CodeAnalysis", "ExcludeFromCodeCoverage"));
        }

        AssignBaseTypes(handlerModel, invokeClass);

        cancellationToken.ThrowIfCancellationRequested();
        HandlerInfoCodeGenerator.Implement(handlerModel, invokeClass);

        CreateConstructor(handlerModel, invokeClass);

        InvokeMethodCodeGenerator.Implement(handlerModel, invokeClass);

        if (handlerModel.RequestParameterInformationList.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();

            BindRequestParametersMethodGenerator.Implement(handlerModel, invokeClass);
            ParametersClassGenerator.GenerateParametersClass(handlerModel, invokeClass);
        }
    }

    private static void AssignBaseTypes(RequestHandlerModel handlerModel, ClassDefinition invokeClass) {
        invokeClass.AddBaseType(
            new GenericTypeDefinition(
                TypeDefinitionEnum.ClassDefinition,
                KnownTypes.Namespace.Hardened.Requests.Runtime.Execution,
                "BaseExecutionHandler",
                new[] {
                    handlerModel.ControllerType
                }));
    }

    /// <remarks>
    /// <c>defaultOutput</c> is always null now. <c>[RawResponse]</c> used to install a
    /// <c>RawOutputHelper.OutputFunc</c> closure here, which <c>ContextSerializationService</c>
    /// checked before any serializer - a second mechanism racing the locator for the same job.
    /// The content type is assigned onto the response instead (see
    /// <c>InvokeMethodCodeGenerator.AssignRawContentType</c>) and <c>RawResponseSerializer</c>
    /// claims it through ordinary selection. The parameter stays because
    /// <c>IExecutionContext.DefaultOutput</c> is public and an application may still set one.
    /// </remarks>
    private static void CreateConstructor(RequestHandlerModel handlerModel, ClassDefinition classDefinition) {
        IOutputComponent defaultOutput = Null();

        if (handlerModel.ResponseInformation.IsAsyncEnumerable) {
            if (handlerModel.RequestParameterInformationList.Count == 0) {
                CreateAsyncEnumerableNoParameterConstructor(handlerModel, classDefinition, defaultOutput);
            }
            else {
                CreateAsyncEnumerableParametersConstructor(handlerModel, classDefinition, defaultOutput);
            }
        }
        else if (handlerModel.RequestParameterInformationList.Count == 0) {
            if (handlerModel.ResponseInformation.IsAsync) {
                CreateAsyncNoParameterConstructor(handlerModel, classDefinition, defaultOutput);
            }
            else {
                CreateSyncNoParameterConstructor(handlerModel, classDefinition, defaultOutput);
            }
        }
        else {
            if (handlerModel.ResponseInformation.IsAsync) {
                CreateAsyncParametersConstructor(handlerModel, classDefinition, defaultOutput);
            }
            else {
                CreateSyncParametersConstructor(handlerModel, classDefinition, defaultOutput);
            }
        }
    }

    private static void CreateAsyncNoParameterConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition,
        IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncStandardFilterEmptyParameters",
            new[] {
                handlerModel.ControllerType
            },
            "serviceProvider",
            "_handlerInfo",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateAsyncParametersConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition, IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncStandardFilterWithParameters",
            new[] {
                handlerModel.ControllerType, GenericParameters
            },
            "serviceProvider",
            "_handlerInfo",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateSyncNoParameterConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition,
        IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "StandardFilterEmptyParameters",
            new[] {
                handlerModel.ControllerType
            },
            "serviceProvider",
            "_handlerInfo",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateSyncParametersConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition, IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "StandardFilterWithParameters",
            new[] {
                handlerModel.ControllerType, GenericParameters
            },
            "serviceProvider",
            "_handlerInfo",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateAsyncEnumerableNoParameterConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition,
        IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncEnumerableFilterEmptyParameters",
            new[] {
                handlerModel.ControllerType,
                handlerModel.ResponseInformation.AsyncEnumerableItemType!
            },
            "serviceProvider",
            "_handlerInfo",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static void CreateAsyncEnumerableParametersConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition, IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncEnumerableFilterWithParameters",
            new[] {
                handlerModel.ControllerType, GenericParameters,
                handlerModel.ResponseInformation.AsyncEnumerableItemType!
            },
            "serviceProvider",
            "_handlerInfo",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
    }

    private static IOutputComponent GenerateFilterEnumerable(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition) {
        // _metadata field is created by HandlerInfoCodeGenerator (before _handlerInfo)
        // to ensure correct static initialization order
        if (handlerModel.Filters.Count > 0) {
            return Invoke(KnownTypes.Requests.ExecutionHelper, "GetFilterInfo", "_metadata");
        }

        return Invoke(KnownTypes.Requests.ExecutionHelper, "GetFilterInfo");
    }
}