using System;
using System.Collections.Generic;
using System.Text;
using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Shared;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Requests
{
    public abstract class BaseRequestModelGenerator
    {
        public virtual RequestHandlerModel GenerateRequestModel(GeneratorSyntaxContext context, CancellationToken cancellationToken)
        {
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

        protected abstract RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation);

        protected abstract ITypeDefinition GetInvokeHandlerType(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation);
        
        protected virtual IReadOnlyList<RequestParameterInformation> GetParameters(
            GeneratorSyntaxContext generatorSyntaxContext, MethodDeclarationSyntax methodDeclaration,
            RequestHandlerNameModel requestHandlerNameModel,
            CancellationToken cancellationToken)
        {
            var parameters = new List<RequestParameterInformation>();

            foreach (var parameter in methodDeclaration.ParameterList.Parameters)
            {
                cancellationToken.ThrowIfCancellationRequested();

                RequestParameterInformation? parameterInformation = 
                    GetParameterInfoFromAttributes(generatorSyntaxContext, methodDeclaration, requestHandlerNameModel, parameter);

                if (parameterInformation == null)
                {
                    parameterInformation = GetParameterInfo(generatorSyntaxContext, methodDeclaration, requestHandlerNameModel, parameter);
                }

                parameters.Add(parameterInformation);
            }

            return parameters;
        }

        protected virtual RequestParameterInformation GetParameterInfo(GeneratorSyntaxContext generatorSyntaxContext,
            MethodDeclarationSyntax methodDeclarationSyntax,
            RequestHandlerNameModel requestHandlerNameModel,
            ParameterSyntax parameter)
        {
            var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;

            if (KnownTypes.Requests.IExecutionContext.Equals(parameterType))
            {
                return new RequestParameterInformation(
                    parameterType,
                    parameter.Identifier.Text,
                    true,
                    null,
                    ParameterBindType.ExecutionContext,
                    string.Empty);
            }

            if (KnownTypes.Requests.IExecutionRequest.Equals(parameterType))
            {
                return new RequestParameterInformation(
                    parameterType,
                    parameter.Identifier.Text,
                    true,
                    null,
                    ParameterBindType.ExecutionRequest,
                    string.Empty);
            }

            if (KnownTypes.Requests.IExecutionResponse.Equals(parameterType))
            {
                return new RequestParameterInformation(
                    parameterType,
                    parameter.Identifier.Text,
                    true,
                    null,
                    ParameterBindType.ExecutionResponse,
                    string.Empty);
            }
            
            if (parameterType.TypeDefinitionEnum == TypeDefinitionEnum.InterfaceDefinition)
            {
                return new RequestParameterInformation(
                    parameterType,
                    parameter.Identifier.Text,
                    !parameterType.IsNullable,
                    null,
                    ParameterBindType.ServiceProvider,
                    string.Empty);
            }

            var id = parameter.Identifier.Text;

            if (requestHandlerNameModel.Path.Contains($"{{{id}}}"))
            {
                return new RequestParameterInformation(
                    parameterType,
                    parameter.Identifier.Text,
                    !parameterType.IsNullable,
                    null,
                    ParameterBindType.Path,
                    string.Empty);
            }

            return new RequestParameterInformation(
                parameterType,
                parameter.Identifier.Text,
                !parameterType.IsNullable,
                null,
                ParameterBindType.Body,
                    string.Empty);
        }

        protected abstract RequestParameterInformation? GetParameterInfoFromAttributes(
            GeneratorSyntaxContext generatorSyntaxContext, MethodDeclarationSyntax methodDeclarationSyntax,
            RequestHandlerNameModel requestHandlerNameModel,
            ParameterSyntax parameter);

        protected virtual string GetControllerMethod(MethodDeclarationSyntax methodDeclaration)
        {
            return methodDeclaration.Identifier.Text;
        }

        protected virtual ITypeDefinition GetControllerType(SyntaxNode contextNode)
        {
            var classDeclarationSyntax =
                contextNode.Ancestors().OfType<ClassDeclarationSyntax>().First();

            var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<NamespaceDeclarationSyntax>().First();

            return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd(), classDeclarationSyntax.Identifier.Text);
        }
        
        protected virtual ResponseInformationModel GetResponseInformation(GeneratorSyntaxContext context, MethodDeclarationSyntax methodDeclaration)
        {
            var templateAttribute =
                context.Node.DescendantNodes()
                    .OfType<AttributeSyntax>()
                    .FirstOrDefault(a => a.Name.ToString() == "Template" || a.Name.ToString() == "TemplateAttribute");

            var template = "";

            if (templateAttribute is { ArgumentList.Arguments.Count: > 0 })
            {
                template = templateAttribute.ArgumentList.Arguments[0].ToString().Trim('"');
            }

            var returnType = methodDeclaration.ReturnType.GetTypeDefinition(context);
            
            var isAsync = false;

            if (returnType is GenericTypeDefinition genericType)
            {
                isAsync = genericType.Name.Equals("Task") || genericType.Name.Equals("ValueTask");
            }
            File.AppendAllText(@"C:\temp\generated\responseInfo.txt", $"{returnType} {isAsync} {returnType.GetType()}\r\n");
            return new ResponseInformationModel(isAsync, template, returnType );
        }
        protected virtual IReadOnlyList<FilterInformationModel> GetFilters(GeneratorSyntaxContext context,
            MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken)
        {
            var filterList = new List<FilterInformationModel>();

            filterList.AddRange(GetFiltersForMethod(context, methodDeclarationSyntax, cancellationToken));
            filterList.AddRange(GetFiltersForClass(context, methodDeclarationSyntax.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault(), cancellationToken));

            return filterList;
        }

        protected virtual IEnumerable<FilterInformationModel> GetFiltersForClass(GeneratorSyntaxContext context,
            ClassDeclarationSyntax? parent, CancellationToken cancellationToken)
        {
            if (parent == null)
            {
                return Enumerable.Empty<FilterInformationModel>();
            }

            return GetFiltersFromAttributes(context, parent.AttributeLists, cancellationToken);
        }

        protected abstract bool IsFilterAttribute(AttributeSyntax attribute);

        protected virtual IEnumerable<FilterInformationModel> GetFiltersForMethod(GeneratorSyntaxContext context,
            MethodDeclarationSyntax methodDeclarationSyntax, CancellationToken cancellationToken)
        {
            return GetFiltersFromAttributes(context, methodDeclarationSyntax.AttributeLists, cancellationToken);
        }

        protected virtual IEnumerable<FilterInformationModel> GetFiltersFromAttributes(GeneratorSyntaxContext context,
            SyntaxList<AttributeListSyntax> attributeListSyntax, CancellationToken cancellationToken)
        {
            foreach (var attributeList in attributeListSyntax)
            {
                foreach (var attribute in attributeList.Attributes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (IsFilterAttribute(attribute))
                    {
                        var operation = context.SemanticModel.GetOperation(attribute);

                        if (operation is { Type: { } })
                        {
                            var arguments = "";
                            var propertyAssignment = "";

                            if (attribute.ArgumentList != null)
                            {
                                foreach (var attributeArgumentSyntax in attribute.ArgumentList.Arguments)
                                {
                                    if (attributeArgumentSyntax.ToString().Contains("="))
                                    {
                                        if (propertyAssignment.Length > 0)
                                        {
                                            propertyAssignment += ", ";
                                        }
                                        propertyAssignment += attributeArgumentSyntax.ToString();
                                    }
                                    else
                                    {
                                        if (arguments.Length > 0)
                                        {
                                            arguments += ", ";
                                        }

                                        arguments += attributeArgumentSyntax.ToString();
                                    }
                                }
                            }

                            yield return new FilterInformationModel(operation.Type.GetTypeDefinition(), arguments, propertyAssignment);
                        }
                    }
                }
            }
        }
    }
}
