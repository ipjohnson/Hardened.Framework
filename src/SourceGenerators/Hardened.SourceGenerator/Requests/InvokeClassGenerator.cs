using CSharpAuthor;
using static CSharpAuthor.SyntaxHelpers;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Hardened.SourceGenerator.Requests;

public static class InvokeClassGenerator {
    /// <summary>
    /// The handler's nested <c>Parameters</c> class, by its full name. This was an empty-namespace
    /// <c>TypeDefinition</c> meaning "resolves here, in the wrapper" - until CSharpAuthor 2.0,
    /// which qualifies global-namespace types in Global mode, and <c>global::Parameters</c> names
    /// nothing.
    /// </summary>
    public static ITypeDefinition ParametersType(RequestHandlerModel handlerModel) =>
        TypeDefinition.Get(handlerModel.InvokeHandlerType.Namespace,
            handlerModel.InvokeHandlerType.Name + ".Parameters");

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

        OutputFactoryGenerator.Implement(handlerModel, invokeClass);

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
    /// <summary>
    /// The path the handler is actually served at, supplied by whoever constructed it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A handler class knows its controller's <c>[BasePath]</c> and its own route template, and
    /// nothing else - the module's <c>[BasePath]</c> lives on the entry point and is applied when
    /// the routing table is built. So a handler served at <c>/catalog/books</c> described itself
    /// as <c>/books</c>, and every consumer of <c>IExecutionRequestHandlerInfo.Path</c> - a
    /// per-handler global filter, an authorization convention, a log line - was handed a path
    /// matching no request the application could receive.
    /// </para>
    /// <para>
    /// Optional, and defaulted to null, because the web routing table is not the only thing that
    /// constructs these: a function handler has no route and no base path, and must keep
    /// compiling against a one-argument constructor.
    /// </para>
    /// </remarks>
    private static void AddRoutePathParameter(MethodDefinition constructor) {
        // string?, not string. Every generated handler lands in a project with nullable
        // reference types on, where "string routePath = null" is CS8625 - a warning locally and
        // an error under TreatWarningsAsErrors, which is what CI builds with.
        var routePath = constructor.AddParameter(
            TypeDefinition.Get(typeof(string)).MakeNullable(), "routePath");

        routePath.DefaultValue = Null();
    }

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
            "_handlerInfo.WithPath(routePath)",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        AddRoutePathParameter(constructor);
    }

    private static void CreateAsyncParametersConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition, IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncStandardFilterWithParameters",
            new[] {
                handlerModel.ControllerType, ParametersType(handlerModel)
            },
            "serviceProvider",
            "_handlerInfo.WithPath(routePath)",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        AddRoutePathParameter(constructor);
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
            "_handlerInfo.WithPath(routePath)",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        AddRoutePathParameter(constructor);
    }

    private static void CreateSyncParametersConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition, IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "StandardFilterWithParameters",
            new[] {
                handlerModel.ControllerType, ParametersType(handlerModel)
            },
            "serviceProvider",
            "_handlerInfo.WithPath(routePath)",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        AddRoutePathParameter(constructor);
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
            "_handlerInfo.WithPath(routePath)",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition),
            FramingArgument(handlerModel)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        AddRoutePathParameter(constructor);
    }

    private static void CreateAsyncEnumerableParametersConstructor(RequestHandlerModel handlerModel,
        ClassDefinition classDefinition, IOutputComponent defaultOutput) {
        var filterMethod = InvokeGeneric(
            KnownTypes.Requests.ExecutionHelper,
            "AsyncEnumerableFilterWithParameters",
            new[] {
                handlerModel.ControllerType, ParametersType(handlerModel),
                handlerModel.ResponseInformation.AsyncEnumerableItemType!
            },
            "serviceProvider",
            "_handlerInfo.WithPath(routePath)",
            "BindRequestParameters",
            "InvokeMethod",
            GenerateFilterEnumerable(handlerModel, classDefinition),
            FramingArgument(handlerModel)
        );
        var constructor = classDefinition.AddConstructor(Base(filterMethod, defaultOutput));

        constructor.AddParameter(typeof(IServiceProvider), "serviceProvider");
        AddRoutePathParameter(constructor);
    }

    /// <summary>
    /// The framing the handler asked for, as the singleton that writes it.
    /// </summary>
    /// <remarks>
    /// <c>null</c> for the default rather than naming <c>NdjsonFraming</c>, so a handler that says
    /// nothing emits exactly the call it emitted before framing existed - the parameter is optional
    /// on the runtime side and the filter falls back to newline-delimited JSON itself.
    /// </remarks>
    private static string FramingArgument(RequestHandlerModel handlerModel) {
        var framing = handlerModel.ResponseInformation.StreamFraming;

        return framing == null
            ? "null"
            : StreamFramingNames.FramingTypeName(framing) + ".Instance";
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