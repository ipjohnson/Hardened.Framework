using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AwsApigatewayv2Integrations;
using Hardened.Shared.Runtime.Attributes;
using HttpMethod = Amazon.CDK.AWS.Apigatewayv2.HttpMethod;

namespace Hardened.Amz.Cdk.Lambda;

[Expose]
public class LambdaApiGatewayStack : IStackDefinition {
    public string NameValue(IStackDeploymentContext context) {
        var functionName = context.GetName(KnownCdkResources.LambdaFunction);
        
        return functionName + "-gateway";
    }

    public IEnumerable<ICdkResourceRef> Consumes => [KnownCdkResources.LambdaFunction];

    public IEnumerable<ICdkResourceRef> Produces => [KnownCdkResources.HttpApi];
    
    public void Deploy(IStackDeploymentContext context) {
        var functionAlias = context.Get(KnownCdkResources.LambdaFunctionAlias);
        var name = context.GetName(KnownCdkResources.LambdaFunctionAlias);
        
        context.Stack.AddDependency(functionAlias.Stack);
        
        var apiGateway = new HttpApi(context.Stack, name + "-gateway", new HttpApiProps {
            ApiName = name, 
            CorsPreflight = new CorsPreflightOptions {
                AllowOrigins = ["*"],
                AllowMethods = [CorsHttpMethod.ANY]
            }
        });
        
        
        var lambdaIntegration = new HttpLambdaIntegration("TemplateIntegration", functionAlias);

        apiGateway.AddRoutes(new AddRoutesOptions {
            Path = "/{proxy+}",
            Methods = [
                HttpMethod.GET,
                HttpMethod.POST,
                HttpMethod.PUT,
                HttpMethod.DELETE
            ],
            Integration = lambdaIntegration
        });
        
        context.Set(KnownCdkResources.HttpApi, apiGateway);
    }
}