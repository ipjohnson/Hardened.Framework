using CSharpAuthor;
using Hardened.SourceGenerator.Models.Request;
using Hardened.SourceGenerator.Requests;
using Hardened.SourceGenerator.Shared;
using Hardened.SourceGenerator.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hardened.SourceGenerator.Function;

public class FunctionModelGenerator : BaseRequestModelGenerator {
    /// <summary>
    /// The attributes that declare a handler rather than bind a parameter, so the binder skips
    /// them. Every trigger is one: <c>[Queue("orders-new")]</c> names a route, it does not describe
    /// an argument.
    /// </summary>
    private readonly List<string> _attributeNames =
        new List<string> { "HardenedFunction" }
            .Concat(TriggerModuleGenerator.Triggers.Select(trigger => trigger.Name))
            .ToList();

    /// <summary>
    /// The route and scheme a handler is registered under.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A trigger routes under its own scheme and the source's own name: <c>[Queue("orders-new")]</c>
    /// becomes <c>QUEUE /orders-new</c>. The scheme has to match what the adapter puts on the
    /// request it builds from a delivered message, or the message arrives and finds no handler.
    /// </para>
    /// <para>
    /// <c>[Event]</c> takes two arguments rather than one, because a bus carries events from many
    /// publishers and neither the source nor the detail type identifies one on its own.
    /// </para>
    /// <para>
    /// <c>[HardenedFunction]</c> keeps <c>POST</c> and the method's own name, which is what it has
    /// always used - a direct invocation has no source to name it after.
    /// </para>
    /// </remarks>
    protected override RequestHandlerNameModel GetRequestNameModel(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation) {
        foreach (var trigger in TriggerModuleGenerator.Triggers) {
            // Every spelling, because the selector that admitted this method accepts every
            // spelling. Looking for the bare name alone let a fully qualified [Queue] through the
            // selector and then find nothing here, so the method fell to the [HardenedFunction]
            // lookup below and the generator threw on a null attribute.
            var triggerAttribute = trigger.Spellings
                .Select(spelling => methodDeclaration.GetAttribute(spelling))
                .FirstOrDefault(found => found != null);

            if (triggerAttribute == null) {
                continue;
            }

            var arguments = triggerAttribute.ArgumentList?.Arguments;

            var path = string.Join(
                "/",
                (arguments ?? default).Select(argument => Value(context, argument)));

            return new RequestHandlerNameModel("/" + path, trigger.Scheme);
        }

        var attribute =
            methodDeclaration.GetAttribute(
                KnownTypes.Requests.HardenedFunctionAttribute.Name.Replace("Attribute", ""))!;
        var argument = attribute.ArgumentList?.Arguments.FirstOrDefault();

        var functionName = methodDeclaration.Identifier.Text;

        if (argument != null) {
            functionName = Value(context, argument);
        }

        return new RequestHandlerNameModel(functionName, "POST");
    }

    /// <summary>
    /// An attribute argument as text, taking the constant where the compiler can fold one.
    /// </summary>
    /// <remarks>
    /// The fallback strips quotes off the written expression, which is what a <c>nameof</c> or an
    /// interpolation the compiler cannot fold arrives as. Both paths existed for
    /// <c>[HardenedFunction]</c> already; the triggers use the same one so a queue named by a
    /// constant routes the same way as one named by a literal.
    /// </remarks>
    private static string Value(GeneratorSyntaxContext context, AttributeArgumentSyntax argument) {
        var constant = context.SemanticModel.GetConstantValue(argument.Expression);

        return constant.HasValue && constant.Value != null
            ? constant.Value.ToString()
            : argument.Expression.ToString().Trim('"');
    }

    protected override ITypeDefinition GetInvokeHandlerType(GeneratorSyntaxContext context,
        MethodDeclarationSyntax methodDeclaration, CancellationToken cancellation) {
        var classDeclarationSyntax =
            methodDeclaration.Ancestors().OfType<ClassDeclarationSyntax>().First();

        var namespaceSyntax = classDeclarationSyntax.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().First();

        var className = classDeclarationSyntax.Identifier + "_" + methodDeclaration.Identifier.Text;

        if (methodDeclaration.ParameterList.Parameters.Count > 0) {
            var parameterString = "";

            foreach (var parameter in methodDeclaration.ParameterList.Parameters) {
                parameterString += '|' + parameter.Identifier.Text;
            }

            className += "_" + parameterString.Select(c => (int)c).Aggregate((total, c) => total + c);
        }

        return TypeDefinition.Get(namespaceSyntax.Name.ToFullString().TrimEnd() + ".Generated", className);
    }

    protected override RequestParameterInformation? GetParameterInfoFromAttributes(
        GeneratorSyntaxContext generatorSyntaxContext, MethodDeclarationSyntax methodDeclarationSyntax,
        RequestHandlerNameModel requestHandlerNameModel,
        ParameterSyntax parameter, int parameterIndex) {
        foreach (var attributeList in parameter.AttributeLists) {
            foreach (var attribute in attributeList.Attributes) {
                // See WebRequestHandlerModelGenerator: these are not binding attributes, and
                // letting one reach the default branch below binds the parameter as a custom
                // attribute instead of from the payload.
                if (NonBindingAttributeFacts.IsNonBinding(generatorSyntaxContext, attribute)) {
                    continue;
                }

                var attributeName = attribute.Name.ToString().Replace("Attribute", "");

                switch (attributeName) {
                    case "FromContext":
                        var headerName =
                            attribute.GetFirstStringArgumentValue(generatorSyntaxContext);

                        return GetParameterInfoWithBinding(generatorSyntaxContext, parameter,
                            ParameterBindType.Header, headerName, parameterIndex);

                    default:
                        return DefaultGetParameterFromAttribute(
                            attribute, generatorSyntaxContext, parameter, parameterIndex);
                }
            }
        }

        return null;
    }

    protected override bool IsFilterAttribute(AttributeSyntax attribute) {
        var attributeName = attribute.Name.ToString().Replace("Attribute", "");

        switch (attributeName) {
            case "Template":
            case "RawResponse":
            case "HardenedFunction":
                return false;

            default:
                return !_attributeNames.Contains(attributeName);
        }
    }

    private static RequestParameterInformation GetParameterInfoWithBinding(
        GeneratorSyntaxContext generatorSyntaxContext,
        ParameterSyntax parameter,
        ParameterBindType bindingType,
        string bindingName,
        int parameterIndex) {
        var parameterType = parameter.Type?.GetTypeDefinition(generatorSyntaxContext)!;

        return CreateRequestParameterInformation(parameter,
            parameterType,
            bindingType,
            parameterIndex,
            null,
            bindingName);
    }
}
