using DependencyModules.Runtime.Attributes;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

public interface ILambdaHttpClientProvider {
    HttpClient GetClient();
}

[SingletonService(Using = RegistrationType.Try)]
public class LambdaHttpClientProvider : ILambdaHttpClientProvider {
    private HttpClient? _client;

    public HttpClient GetClient() {
        return _client ??= new HttpClient {
            BaseAddress = new Uri(
                "http://" + Environment.GetEnvironmentVariable("AWS_LAMBDA_RUNTIME_API")),
            Timeout = TimeSpan.FromDays(1)
        };
    }
}
