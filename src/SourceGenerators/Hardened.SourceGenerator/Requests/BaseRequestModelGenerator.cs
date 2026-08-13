using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests;

public abstract class BaseRequestModelGenerator {
    public virtual RequestHandlerModel GenerateRequestModel(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken) {
        cancellationToken.ThrowIfCancellationRequested();

        var methodDeclaration = (MethodDeclarationSyntax)context.Node;

        var methodName = GetControllerMethod(methodDeclaration);
        var controllerType = GetControllerType(methodDeclaration);
        var response = GetResponseInformation(context, methodDeclaration);
        var filters = GetFilters(context, methodDeclaration, cancellationToken);

        var nameModel = GetRequestNameModel(context, methodDeclaration, cancellationToken);

        return new RequestHandlerModel(
            nameModel,
            controllerType,
            methodName,
            GetInvokeHandlerType(context, methodDeclaration, cancellationToken),
            GetParameters(context, methodDeclaration, nameModel, cancellationToken),
            response,
            filters);
    }

    protected abstract RequestHandlerNameModel GetRequestNameModel(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation);

    protected abstract ITypeDefinition GetInvokeHandlerType(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration,
        CancellationToken cancellation);

    protected virtual IReadOnlyList<RequestParameterInformation> GetParameters(
        GeneratorSyntaxContext generatorSyntaxContext,
        MethodDeclarationSyntax methodDeclaration,
        RequestHandlerNameModel requestHandlerNameModel,
        CancellationToken cancellationToken) {
        var parameters = new List<RequestParameterInformation>();
        for(var i = 0; i < methodDeclaration.ParameterList.Parameters.Count; i++) {
            var parameter = methodDeclaration.ParameterList.Parameters[i];
            cancellationToken.ThrowIfCancellationRequested();

            RequestParameterInformation? parameterInformation =
                GetParameterInfoFromAttributes(generatorSyntaxContext, methodDeclaration,
                    requestHandlerNameModel,
                    parameter,
                    i);

            if (parameterInformation == null) {
                parameterInformation = GetParameterInfo(
                    generatorSyntaxContext, 
                    methodDeclaration,
                    requestHandlerNameModel, 
                    parameter,
                    i);
            }

            parameters.Add(parameterInformation);
        }

        return parameters;
    }

    protected virtual RequestParameterInformation? DefaultGetParameterFromAttribute(
        AttributeSyntax attribute, 
        GeneratorSyntaxContext generatorSyntaxContext, 
        ParameterSyntax parameter, 
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;
        var name = parameter.Identifier.Text;

        string? defaultValue = null;

        if (parameter.Default != null) {
            defaultValue = parameter.Default.Value.ToFullString();
        }

        return new RequestParameterInformation(
                parameterType,
                name,
                !parameterType.IsNullable,
                defaultValue,
                ParameterBindType.CustomAttribute,
                "",
                parameterIndex,
                AttributeModelHelper.GetAttribute(generatorSyntaxContext, attribute)
                );
    }

    
    protected virtual RequestParameterInformation GetParameterInfo(
        GeneratorSyntaxContext generatorSyntaxContext,
        MethodDeclarationSyntax methodDeclarationSyntax,
        RequestHandlerNameModel requestHandlerNameModel,
        ParameterSyntax parameter,
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext);

        // Resolution returns null for a name the compiler cannot bind, which happens constantly in
        // an editor - a signature is briefly invalid on the way to being valid, mid-rename or
        // before the model class is written. This used to carry a null forward behind a `!` and
        // dereference a few lines down, which threw out of the syntax transform and cost the whole
        // assembly its generated code, not just this handler. Now the parameter is recorded as
        // unresolved and the handler is skipped at the output stage, where a diagnostic can
        // actually be reported.
        if (parameterType == null) {
            return new RequestParameterInformation(
                TypeDefinition.Get("", parameter.Type?.ToString() ?? "?"),
                parameter.Identifier.Text,
                false,
                null,
                ParameterBindType.Unresolved,
                parameter.Identifier.Text,
                parameterIndex);
        }

        if (KnownTypes.Requests.IExecutionContext.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ExecutionContext,
                parameterIndex,
                true);
        }

        if (KnownTypes.Requests.IExecutionRequest.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ExecutionRequest,
                parameterIndex,
                true);
        }

        if (KnownTypes.Requests.IExecutionResponse.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ExecutionResponse,
                parameterIndex,
                true);
        }

        if (KnownTypes.DI.IServiceProvider.Equals(parameterType)) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.ServiceProvider,parameterIndex);
        }

        if (parameterType.TypeDefinitionEnum == TypeDefinitionEnum.InterfaceDefinition) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.FromServiceProvider,parameterIndex);
        }

        var id = parameter.Identifier.Text;

        if (requestHandlerNameModel.Path.Contains($"{{{id}}}")) {
            return CreateRequestParameterInformation(parameter, parameterType,
                ParameterBindType.Path,parameterIndex);
        }

        return CreateRequestParameterInformation(parameter, parameterType, ParameterBindType.Body,parameterIndex);
    }

    public static RequestParameterInformation CreateRequestParameterInformation(
        ParameterSyntax parameter,
        ITypeDefinition parameterType,
        ParameterBindType parameterBindType,
        int parameterIndex,
        bool? required = null,
        string? bindingName = null,
        AttributeModel? customAttribute = null) {
        if (!parameterType.IsNullable && parameter.ToFullString().Contains("?")) {
            parameterType = parameterType.MakeNullable();
        }

        string? defaultValue = null;

        if (parameter.Default != null) {
            defaultValue = parameter.Default.Value.ToFullString();
        }
        
        return new RequestParameterInformation(
            parameterType,
            parameter.Identifier.Text,
            required ?? !parameterType.IsNullable,
            defaultValue,
            parameterBindType,
            bindingName ?? string.Empty,
            parameterIndex,
            customAttribute);
    }

    protected abstract RequestParameterInformation? GetParameterInfoFromAttributes(
        GeneratorSyntaxContext generatorSyntaxContext,
        MethodDeclarationSyntax methodDeclarationSyntax,
        RequestHandlerNameModel requestHandlerNameModel,
        ParameterSyntax parameter,
        int parameterIndex);

    protected virtual string GetControllerMethod(MethodDeclarationSyntax methodDeclaration) {
        return methodDeclaration.Identifier.Text;
    }

    protected virtual ITypeDefinition GetControllerType(SyntaxNode contextNode) {
        var classDeclarationSyntax =
            contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

        var namespaceSyntax = classDeclarationSyntax.Ancestors()
            .OfType<BaseNamespaceDeclarationSyntax>().First();

        return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(),
            classDeclarationSyntax.Identifier.Text);
    }

    protected virtual ResponseInformationModel GetResponseInformation(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration) {
        var returnType = methodDeclaration.ReturnType.GetTypeDefinition(context);

        var isAsync = false;
        var isAsyncEnumerable = false;
        ITypeDefinition? asyncEnumerableItemType = null;

        if (returnType is GenericTypeDefinition genericType) {
            if (genericType.Name.Equals("Task") || genericType.Name.Equals("ValueTask")) {
                isAsync = true;
            } else if (genericType.Name.Equals("IAsyncEnumerable")) {
                isAsyncEnumerable = true;
                asyncEnumerableItemType = genericType.TypeArguments[0];
            }
        } else if (returnType?.Name == "Task") {
            isAsync = true;
            returnType = TypeDefinition.Get(typeof(void));
        }

        var rawResponse = "";
        var varResponseAttribute = context.Node.GetAttribute("RawResponse");

        if (varResponseAttribute != null) {
            rawResponse =
                varResponseAttribute.ArgumentList?.Arguments[0].ToString().Trim('"') ??
                "text/plain";
        }

        return new ResponseInformationModel {
            IsAsync = isAsync,
            IsAsyncEnumerable = isAsyncEnumerable,
            AsyncEnumerableItemType = asyncEnumerableItemType,
            ReturnType = returnType,
            RawResponseContentType = rawResponse
        };
    }

    protected virtual IReadOnlyList<AttributeModel> GetFilters(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclarationSyntax,
        CancellationToken cancellationToken) {
        var filterList = new List<AttributeModel>();

        filterList.AddRange(
            GetFiltersForMethod(context, methodDeclarationSyntax, cancellationToken));
        filterList.AddRange(GetFiltersForClass(context,
            methodDeclarationSyntax.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault(),
            cancellationToken));

        return filterList;
    }

    protected virtual IEnumerable<AttributeModel> GetFiltersForClass(
        GeneratorSyntaxContext context,
        ClassDeclarationSyntax? parent,
        CancellationToken cancellationToken) {
        if (parent == null) {
            return Enumerable.Empty<AttributeModel>();
        }

        return GetFiltersFromAttributes(context, parent.AttributeLists, cancellationToken);
    }

    protected abstract bool IsFilterAttribute(AttributeSyntax attribute);

    protected virtual IEnumerable<AttributeModel> GetFiltersForMethod(
        GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclarationSyntax,
        CancellationToken cancellationToken) {
        return GetFiltersFromAttributes(context, methodDeclarationSyntax.AttributeLists,
            cancellationToken);
    }

    protected virtual IEnumerable<AttributeModel> GetFiltersFromAttributes(
        GeneratorSyntaxContext context,
        SyntaxList<AttributeListSyntax> attributeListSyntax,
        CancellationToken cancellationToken) {

        return AttributeModelHelper.GetAttributes(
            context,
            attributeListSyntax,
            cancellationToken,
            IsFilterAttribute);
    }
}