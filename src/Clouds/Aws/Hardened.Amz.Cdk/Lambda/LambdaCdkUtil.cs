using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Amazon.CDK.AwsApigatewayv2Integrations;
using DependencyModules.Runtime.Attributes;
using HttpMethod = Amazon.CDK.AWS.Apigatewayv2.HttpMethod;

namespace Hardened.Amz.Cdk.Lambda;

public class LambdaRequest {
    
    public string Name { get; set; } = string.Empty;

    public string DistLocation { get; set; } = "../dist/function.zip";
    

    public string? AliasName { get; set; } = "live";

    public bool UseCodeDeploy { get; set; } = true;
    
    public Action<FunctionProps> Props { get; set; } = props => { };
    
    public Type ApplicationType { get; set; } = default!;
}

public class HttpApiLambdaRequest : LambdaRequest {
    
    public Action<HttpApiProps> HttpApiProps { get; set; } = props => {};

    public Action<HttpApi, Alias> ConfigureApi { get; set; } = (api, alias) => {
        var lambdaIntegration  = new HttpLambdaIntegration("LambdaIntegration", alias);
        
        api.AddRoutes(new AddRoutesOptions {
            Path = "/{proxy+}",
            Methods = [
                HttpMethod.ANY,
            ],
            Integration = lambdaIntegration
        });
    };
    
}

[TransientService]
public class LambdaCdkUtil {
    private readonly StackContextAccessor _stackContextAccessor;
    private readonly IDefaultFunctionProps _defaultFunctionProps;

    public LambdaCdkUtil(
        StackContextAccessor stackContextAccessor, 
        IDefaultFunctionProps defaultFunctionProps) {
        _stackContextAccessor = stackContextAccessor;
        _defaultFunctionProps = defaultFunctionProps;
    }

    public (Function function, HttpApi api) HttpApiFunctionCreate(HttpApiLambdaRequest request) {
        var context = _stackContextAccessor.Context;
        var (lambdaFunction, alias) = LambdaFunctionCreate(request);

        var apiProps = new HttpApiProps {
            ApiName = request.Name,
            CorsPreflight = new CorsPreflightOptions {
                AllowOrigins = ["*"],
                AllowMethods = [CorsHttpMethod.ANY]
            },
        };
        
        request.HttpApiProps(apiProps);
        
        var apiGateway = new HttpApi(context.Stack, request.Name + "-gateway",apiProps);
        
        request.ConfigureApi(apiGateway, alias);
        
        return (lambdaFunction, apiGateway);
    }

    public (Function function, Alias? alias) LambdaFunctionCreate(LambdaRequest request) {
        var context = _stackContextAccessor.Context;

        var assemblyName = request.ApplicationType.Assembly.GetName().Name;
        var handlerName = $"{assemblyName}::{request.ApplicationType.FullName}::Invoke";

        var lambdaProps = new FunctionProps {
            Runtime = Runtime.DOTNET_8,
            MemorySize = 768,
            // TODO: migrate to LogGroup. CDK deprecated LogRetention in favour of passing
            // an explicit LogGroup, but the two produce different CloudFormation - the
            // former provisions a log-retention custom resource, the latter an actual
            // LogGroup resource. Switching changes the deployed stack, so it needs a
            // deliberate infrastructure decision rather than a warning cleanup.
#pragma warning disable CS0618
            LogRetention = RetentionDays.ONE_MONTH,
#pragma warning restore CS0618
            FunctionName = request.Name,
            Handler = handlerName,
            Code = Code.FromCustomCommand(request.DistLocation,
                [
                    $"dotnet-lambda package -pl ../{assemblyName} -o {request.DistLocation}"
                ],
                new CustomCommandOptions {
                    CommandOptions = new Dictionary<string, object> {
                        {
                            "shell", true
                        }
                    }
                })
        };
        
        _defaultFunctionProps.ApplyDefaults(lambdaProps);

        request.Props(lambdaProps);

        var lambdaFunction = new Function(context.Stack, request.Name + "-function", lambdaProps);

        context.Set(KnownCdkResources.LambdaFunction, lambdaFunction, request.Name);
        
        if (request.AliasName != null) {
            var alias = lambdaFunction.AddAlias(request.AliasName);

            context.Set(KnownCdkResources.LambdaFunctionAlias, alias, request.Name);
            
            return (lambdaFunction, alias);
        }
        
        return (lambdaFunction, null);
    }

    private void ValidateRequest(HttpApiLambdaRequest request) {

    }
}