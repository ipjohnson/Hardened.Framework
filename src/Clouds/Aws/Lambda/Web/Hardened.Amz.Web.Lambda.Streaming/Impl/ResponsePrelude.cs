using System.Text.Json.Serialization;

namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public class ResponsePrelude {
    [JsonPropertyName("statusCode")]
    public int StatusCode { get; init; }

    [JsonPropertyName("headers")]
    public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    [JsonPropertyName("cookies")]
    public IEnumerable<string> Cookies { get; init; } = Array.Empty<string>();
}

public class LambdaErrorResponse {
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = string.Empty;

    [JsonPropertyName("errorType")]
    public string ErrorType { get; init; } = string.Empty;
}
