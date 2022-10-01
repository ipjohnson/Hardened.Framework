namespace Hardened.Web.Lambda.Runtime;

public enum ProxyIntegrationType
{
    ApiGateway,
    HttpApiV2
}

public class LambdaWebApplicationAttribute : Attribute
{
    public ProxyIntegrationType Version { get; set; }
}