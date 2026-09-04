using Amazon.CDK.AWS.Apigatewayv2;
using Amazon.CDK.AWS.Lambda;

namespace Hardened.Amz.Cdk;

public interface ICdkResourceRef {
    string Name { get; }

    Type TypeOfResource { get; }
}

public record CdkResourceRef<T>(string Name = "default") : ICdkResourceRef {
    public Type TypeOfResource => typeof(T);
}

public static class KnownCdkResources {
    public static CdkResourceRef<Function> LambdaFunction = new ();
    
    public static CdkResourceRef<Alias> LambdaFunctionAlias = new ();
    
    public static CdkResourceRef<HttpApi> HttpApi = new ();

    public static CdkResourceRef<FunctionUrl> LambdaFunctionUrl = new ();
}
