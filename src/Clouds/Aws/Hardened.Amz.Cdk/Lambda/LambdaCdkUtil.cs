using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.Logs;
using Hardened.Shared.Runtime.Attributes;

namespace Hardened.Amz.Cdk.Lambda;

public class LambdaRequest {
    
    public string Name { get; set; } = string.Empty;

    public int MemorySize { get; set; } = 784;
    
    public bool Mutating { get; set; } = false;

    public string DistLocation { get; set; } = "../dist/function.zip";

    public string AliasName = "live";
}

public class HttpApiLambdaRequest : LambdaRequest {
    public Type ApplicationType { get; set; } = default!;
}

[Expose]
public class LambdaCdkUtil {
    private StackContextAccessor _stackContextAccessor;

    public LambdaCdkUtil(StackContextAccessor stackContextAccessor) {
        _stackContextAccessor = stackContextAccessor;
    }

    public Function HttpApiFunction(HttpApiLambdaRequest request) {
        var context = _stackContextAccessor.Context;

        ValidateRequest(request);
        var assemblyName = request.ApplicationType.Assembly.GetName().Name;
        var handlerName = $"{assemblyName}::{request.ApplicationType.FullName}::Invoke";

        var lambdaFunction = new Function(context.Stack, request.Name + "-function", new FunctionProps {
            Runtime = Runtime.DOTNET_8,
            MemorySize = request.MemorySize,
            LogRetention = RetentionDays.ONE_MONTH,
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
        });

        var alias = lambdaFunction.AddAlias(request.AliasName);

        context.Set(KnownCdkResources.LambdaFunction, lambdaFunction, request.Name);
        context.Set(KnownCdkResources.LambdaFunctionAlias, alias, request.Name);

        return lambdaFunction;
    }

    private void ValidateRequest(HttpApiLambdaRequest request) { }
}