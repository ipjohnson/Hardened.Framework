using Hardened.Amz.Web.Lambda.Runtime.Impl;

namespace Hardened.Amz.Web.Lambda.Harness;

public static class AspNetExtensions
{
    public static void AddLambdaApplication<T>(this IServiceCollection serviceCollection) where T : IApiGatewayV2Handler, new()
    {
        serviceCollection.AddSingleton<IRequestToLambdaService, RequestToLambdaService<T>>();
    }

    public static void UseLambdaApplication(this WebApplication webApplication)
    {
        var service = webApplication.Services.GetRequiredService<IRequestToLambdaService>();

        webApplication.Use(service.HandleRequest);
    }
}