using System.Text.Json.Serialization;

namespace Hardened.Amz.Function.Lambda.Streaming.Impl;

public class LambdaErrorResponse {
    [JsonPropertyName("errorMessage")]
    public string ErrorMessage { get; init; } = string.Empty;

    [JsonPropertyName("errorType")]
    public string ErrorType { get; init; } = string.Empty;
}
