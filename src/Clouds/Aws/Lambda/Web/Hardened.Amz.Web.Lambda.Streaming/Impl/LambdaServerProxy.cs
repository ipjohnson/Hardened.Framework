using System.IO.Pipelines;
using System.Net.Http.Headers;
using System.Text.Json;
using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using Hardened.Amz.Web.Lambda.Streaming.Context;
using Hardened.Amz.Web.Lambda.Streaming.Serializer;
namespace Hardened.Amz.Web.Lambda.Streaming.Impl;

public class InvocationData {
    public required APIGatewayHttpApiV2ProxyRequest Request { get; init; }
    public required ILambdaContext LambdaContext { get; init; }
    public required string RequestId { get; init; }
}

public interface ILambdaServerProxy {
    Task<InvocationData> GetNextInvocation(CancellationToken ct);
    Task SendResponse(string requestId, PipeReader reader, CancellationToken ct);
    Task ReportError(string requestId, Exception ex, CancellationToken ct);
}

public class LambdaServerProxy : ILambdaServerProxy {
    private readonly ILambdaHttpClientProvider _clientProvider;

    private static readonly MediaTypeHeaderValue StreamingContentType =
        MediaTypeHeaderValue.Parse("application/vnd.awslambda.http-integration-response");

    public LambdaServerProxy(ILambdaHttpClientProvider clientProvider) {
        _clientProvider = clientProvider;
    }

    public async Task<InvocationData> GetNextInvocation(CancellationToken ct) {
        var client = _clientProvider.GetClient();

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Get, "/2018-06-01/runtime/invocation/next");

        var response = await client.SendAsync(httpRequest, ct);
        response.EnsureSuccessStatusCode();

        var rawPayload = await response.Content.ReadAsStringAsync(ct);
        // Use Console directly since ILambdaContext is not yet set on the accessor
        Console.Error.WriteLine($"[LambdaServerProxy] Raw payload: {rawPayload}");

        var request = JsonSerializer.Deserialize(
            rawPayload,
            StreamingEventSerializerContext.Default.APIGatewayHttpApiV2ProxyRequest);

        if (request == null) {
            throw new InvalidOperationException("Failed to deserialize invocation request.");
        }

        Console.Error.WriteLine(
            $"[LambdaServerProxy] Deserialized - Method: {request.RequestContext?.Http?.Method ?? "NULL"}, Path: {request.RawPath ?? "NULL"}, RC null: {request.RequestContext == null}, Http null: {request.RequestContext?.Http == null}");

        var requestId = response.Headers
            .GetValues("Lambda-Runtime-Aws-Request-Id").First();

        var lambdaContext = CreateLambdaContext(response, requestId);

        return new InvocationData {
            Request = request,
            LambdaContext = lambdaContext,
            RequestId = requestId
        };
    }

    public async Task SendResponse(string requestId, PipeReader reader, CancellationToken ct) {
        var client = _clientProvider.GetClient();

        var streamContent = new ResponsiveStreamContent(reader.AsStream(true));
        streamContent.Headers.ContentType = StreamingContentType;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/2018-06-01/runtime/invocation/{requestId}/response") {
            Content = streamContent
        };

        request.Headers.Add("Lambda-Runtime-Function-Response-Mode", "streaming");
        request.Headers.TransferEncodingChunked = true;

        var response = await client.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    public async Task ReportError(string requestId, Exception ex, CancellationToken ct) {
        var client = _clientProvider.GetClient();

        var errorResponse = new LambdaErrorResponse {
            ErrorMessage = ex.Message,
            ErrorType = ex.GetType().Name
        };

        var json = JsonSerializer.Serialize(
            errorResponse, StreamingEventSerializerContext.Default.LambdaErrorResponse);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/2018-06-01/runtime/invocation/{requestId}/error") {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        };

        try {
            var response = await client.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
        }
        catch {
            // Best effort error reporting
        }
    }

    private static ILambdaContext CreateLambdaContext(
        HttpResponseMessage response, string requestId) {
        string? functionArn = null;
        long deadlineMs = 0;
        string? traceId = null;

        if (response.Headers.TryGetValues(
                "Lambda-Runtime-Invoked-Function-Arn", out var arnValues)) {
            functionArn = arnValues.First();
        }

        if (response.Headers.TryGetValues(
                "Lambda-Runtime-Deadline-Ms", out var deadlineValues)) {
            long.TryParse(deadlineValues.First(), out deadlineMs);
        }

        if (response.Headers.TryGetValues(
                "Lambda-Runtime-Trace-Id", out var traceValues)) {
            traceId = traceValues.First();
            Environment.SetEnvironmentVariable("_X_AMZN_TRACE_ID", traceId);
        }

        return new InternalLambdaContext(
            requestId,
            functionArn ?? string.Empty,
            deadlineMs,
            Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_NAME") ?? string.Empty,
            Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_VERSION") ?? string.Empty,
            Environment.GetEnvironmentVariable("AWS_LAMBDA_LOG_GROUP_NAME") ?? string.Empty,
            Environment.GetEnvironmentVariable("AWS_LAMBDA_LOG_STREAM_NAME") ?? string.Empty,
            int.TryParse(Environment.GetEnvironmentVariable("AWS_LAMBDA_FUNCTION_MEMORY_SIZE"), out var mem)
                ? mem
                : 128);
    }
}
